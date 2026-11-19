// Hallow Hub v1.9 - a quick PC check before you play (CPU, RAM, GPU, free space), and the recent-games list
// Hallow Toolkit's phone dashboard uses to start games.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HallowHub
{
    public class PcState { public double CpuLoad = -1, RamFreeGB = -1, RamTotalGB = -1, GpuTemp = -1, GpuLoad = -1; }

    public static class Health
    {
        [StructLayout(LayoutKind.Sequential)]
        class MEMORYSTATUSEX { public uint dwLength = 64; public uint dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

        static PerformanceCounter cpu;
        static string smi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");

        // slow-ish (CPU counter + nvidia-smi): call from a background thread
        public static PcState Read()
        {
            var s = new PcState();
            try { var m = new MEMORYSTATUSEX(); if (GlobalMemoryStatusEx(m)) { s.RamFreeGB = m.ullAvailPhys / 1073741824.0; s.RamTotalGB = m.ullTotalPhys / 1073741824.0; } } catch { }
            try
            {
                if (cpu == null) { cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total"); cpu.NextValue(); Thread.Sleep(500); }
                s.CpuLoad = cpu.NextValue();
            }
            catch { }
            if (File.Exists(smi))
                try
                {
                    var p = Process.Start(new ProcessStartInfo(smi, "--query-gpu=temperature.gpu,utilization.gpu --format=csv,noheader,nounits") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
                    var line = p.StandardOutput.ReadLine(); p.WaitForExit(3000);
                    var parts = (line ?? "").Split(','); double t, u;
                    if (parts.Length >= 2 && double.TryParse(parts[0].Trim(), out t) && double.TryParse(parts[1].Trim(), out u)) { s.GpuTemp = t; s.GpuLoad = u; }
                }
                catch { }
            return s;
        }
    }

    public partial class HubWindow
    {
        PcState pc; bool pcReading; WrapPanel pcPills;

        void RefreshPc()
        {
            if (pcReading || (!IsVisible && !Snap.On)) return;
            pcReading = true;
            if (Snap.On) { pc = Health.Read(); pcReading = false; ShowPc(); return; }
            new Thread(() => { var r = Health.Read(); Dispatcher.BeginInvoke(new Action(() => { pc = r; pcReading = false; ShowPc(); })); }) { IsBackground = true }.Start();
        }

        void ShowPc()
        {
            if (pcPills == null) return;
            pcPills.Children.Clear();
            Action<string, string, int> pill = (icon, text, level) =>   // level: 0 fine, 1 warn, 2 bad
            {
                var col = level == 2 ? "#F05A5A" : level == 1 ? "#F0BE5A" : "#CFC8DC";
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var ic = Icons.Make(icon, 10, B(col)); ic.Margin = new Thickness(0, 0, 5, 0); sp.Children.Add(ic);
                sp.Children.Add(new TextBlock { Text = text, FontSize = 11, Foreground = B(col) });
                pcPills.Children.Add(new Border { Child = sp, Margin = new Thickness(0, 0, 10, 4) });
            };
            if (pc != null)
            {
                if (pc.CpuLoad >= 0) pill("microchip", "CPU " + pc.CpuLoad.ToString("0") + "%", pc.CpuLoad >= 85 ? 1 : 0);
                if (pc.RamFreeGB >= 0) pill("memory", pc.RamFreeGB.ToString("0.0") + " GB RAM free", pc.RamFreeGB < 2 ? 2 : pc.RamFreeGB < 4 ? 1 : 0);
                if (pc.GpuTemp >= 0) pill("temperature-half", "GPU " + pc.GpuTemp.ToString("0") + "°C", pc.GpuTemp >= 85 ? 2 : pc.GpuTemp >= 78 ? 1 : 0);
            }
            if (sel != null)
            {
                var drive = PlayCopy(sel).Drive;
                try { if (drive != null) { var di = new DriveInfo(drive); if (di.IsReady) { var gb = di.AvailableFreeSpace / 1073741824.0; pill("hard-drive", drive + " " + gb.ToString("0") + " GB free", gb < 10 ? 2 : gb < 30 ? 1 : 0); } } } catch { }
            }
            if (pcPills.Children.Count == 0) pcPills.Children.Add(new TextBlock { Text = "Checking...", FontSize = 11, Foreground = (Brush)FindResource("Muted") });
        }

        // ---- the phone dashboard (Hallow Toolkit) reads this to show Play buttons for your recent games
        string lastRecent;
        void WriteRecent()
        {
            if (Snap.On) return;
            try
            {
                var list = games.Where(x => !S.For(x.Id).Hidden).OrderByDescending(x => LastOf(x) ?? DateTime.MinValue).ThenByDescending(x => Total(x)).Take(8)
                    .Select(x => new Dictionary<string, object> { { "id", x.Id }, { "name", x.Name }, { "playing", IsRunning(x) } }).ToList();
                var json = new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "exe", System.Reflection.Assembly.GetExecutingAssembly().Location }, { "games", list } });
                if (json == lastRecent) return;
                Directory.CreateDirectory(HubSettings.Dir); File.WriteAllText(Path.Combine(HubSettings.Dir, "recent.json"), json); lastRecent = json;
            }
            catch { }
        }
    }
}
