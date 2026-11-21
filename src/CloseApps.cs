// Hallow Hub v2.12 - a launch checklist: apps to close when a game starts (a browser, a sync app, a heavy editor), opened
// again when it closes. Closing is like clicking X (they can still ask to save) - nothing is ever force-killed, and system
// parts, launchers and the Hallow apps can't be picked. Together with the Mixer preset, the Hallow Toolkit game session
// (Boost apps and High performance power) and the apps that open with a game, the "When this game starts" card lists it all.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HallowHub
{
    public static class CloseApps
    {
        // never closed: Windows itself, game launchers (the game needs them), consoles (Hallow Toolkit runs in one) and the Hallow apps
        static readonly HashSet<string> Never = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "explorer", "dwm", "csrss", "winlogon", "wininit", "smss", "services", "lsass", "svchost", "system", "idle", "registry", "fontdrvhost", "sihost", "taskhostw",
            "ctfmon", "searchhost", "searchapp", "startmenuexperiencehost", "shellexperiencehost", "textinputhost", "runtimebroker", "applicationframehost", "lockapp",
            "securityhealthsystray", "msmpeng", "audiodg", "taskmgr", "conhost", "cmd", "powershell", "pwsh", "windowsterminal", "openconsole", "mmc", "systemsettings",
            "steam", "steamwebhelper", "steamservice", "battle.net", "agent", "epicgameslauncher", "epicwebhelper", "eadesktop", "eabackgroundservice", "ealauncher",
            "ubisoftconnect", "upc", "uplaywebcore", "galaxyclient", "galaxyclienthelper", "riotclientservices", "riotclientux", "riotclientcrashhandler", "xboxpcapp", "gamingservices", "gamebar",
            "hallowhub", "hallowmixer", "hallowoverlay", "hallowclips", "hallowvault", "hallowkeys", "hallowremote", "hallowsetup", "hallowtoolkit", "hallowguard", "hallowassist" };

        public static bool Allowed(string exeOrName)
        {
            var n = Path.GetFileNameWithoutExtension((exeOrName ?? "").Trim());
            if (n.Length == 0 || Never.Contains(n)) return false;
            try { if (string.Equals(n, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase)) return false; } catch { }
            return true;
        }

        public static string Name(string exe)
        {
            try { if (File.Exists(exe)) { var n = FileVersionInfo.GetVersionInfo(exe).FileDescription; if (!string.IsNullOrWhiteSpace(n)) return n.Trim(); } } catch { }
            return Path.GetFileNameWithoutExtension(exe ?? "");
        }

        // apps with a window open right now that could be closed: exe path -> name (one per program)
        public static List<KeyValuePair<string, string>> Running()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var l = new List<KeyValuePair<string, string>>();
            var me = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == me || p.MainWindowHandle == IntPtr.Zero || !Allowed(p.ProcessName) || seen.Contains(p.ProcessName)) continue;
                    string path = null; try { path = p.MainModule.FileName; } catch { }
                    if (path == null || path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)) continue;
                    seen.Add(p.ProcessName); l.Add(new KeyValuePair<string, string>(path, Name(path)));
                }
                catch { }
                finally { p.Dispose(); }
            }
            return l.OrderBy(x => x.Value, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public class Result
        {
            public List<string> Closed = new List<string>(), Reopen = new List<string>(), NoWindow = new List<string>();
        }

        // asks each app to close (like clicking X); skips anything running from the game's own folder
        public static Result Close(IEnumerable<string> entries, string gameDir)
        {
            var r = new Result(); var me = Process.GetCurrentProcess().Id;
            foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Allowed(entry)) continue;
                var name = Path.GetFileNameWithoutExtension(entry);
                Process[] procs; try { procs = Process.GetProcessesByName(name); } catch { continue; }
                if (procs.Length == 0) continue;
                bool asked = false, any = false; string path = null;
                foreach (var p in procs)
                {
                    try
                    {
                        if (p.Id == me) continue;
                        string pp = null; try { pp = p.MainModule.FileName; } catch { }
                        if (pp != null && !string.IsNullOrEmpty(gameDir) && pp.StartsWith(gameDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) continue;   // part of the game
                        any = true; if (path == null) path = pp;
                        if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow()) asked = true;
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
                if (!any) continue;
                var shown = Name(path ?? entry);
                if (asked) { r.Closed.Add(shown); var again = path != null && File.Exists(path) ? path : File.Exists(entry) ? entry : null; if (again != null) r.Reopen.Add(again); }
                else r.NoWindow.Add(shown);
            }
            return r;
        }

        // opens them again (minimized) unless they're already back; returns the names opened
        public static List<string> Reopen(IEnumerable<string> exes)
        {
            var names = new List<string>();
            foreach (var exe in exes.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)).Length > 0) continue;
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe), WindowStyle = ProcessWindowStyle.Minimized });
                    names.Add(Name(exe));
                }
                catch { }
            }
            return names;
        }
    }

    public partial class HubWindow
    {
        readonly Dictionary<string, List<string>> closedFor = new Dictionary<string, List<string>>();   // game id -> apps to open again

        List<string> CloseBefore(Game g)
        {
            var said = new List<string>(); var d = S.For(g.Id);
            if (d.CloseFirst.Count == 0 || closedFor.ContainsKey(g.Id)) return said;
            var r = CloseApps.Close(d.CloseFirst, PlayCopy(g).InstallDir);
            closedFor[g.Id] = r.Reopen;
            if (r.Closed.Count > 0) said.Add("closed " + string.Join(", ", r.Closed));
            if (r.NoWindow.Count > 0) said.Add(string.Join(", ", r.NoWindow) + " has no window to close (use its tray icon)");
            return said;
        }
        List<string> ReopenAfter(Game g)
        {
            List<string> l; if (!closedFor.TryGetValue(g.Id, out l)) return new List<string>();
            closedFor.Remove(g.Id);
            if (S.For(g.Id).NoReopen || l.Count == 0) return new List<string>();
            return CloseApps.Reopen(l);
        }

        TextBlock checklist;
        FrameworkElement BuildChecklist()
        {
            checklist = new TextBlock { Foreground = B("#CFC8DC"), FontSize = 11, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Cursor = Cursors.Hand, Visibility = Visibility.Collapsed,
                ToolTip = "Change it in Game settings" };
            checklist.MouseLeftButtonUp += (s, e) => GameSettings(sel);
            return checklist;
        }
        void ShowChecklist(Game g, GameData d)
        {
            if (checklist == null) return;
            var lines = new List<string>();
            var allowed = d.CloseFirst.Where(CloseApps.Allowed).ToList();
            var close = allowed.Select(x => File.Exists(x) ? CloseApps.Name(x) : Path.GetFileNameWithoutExtension(x)).ToList();
            if (close.Count > 0) lines.Add("\u2715  Closes " + string.Join(", ", close) + (d.NoReopen ? "" : " (back after)"));
            var open = d.OpenWith.Where(File.Exists).Select(CloseApps.Name).ToList();
            if (open.Count > 0) lines.Add("\u2713  Opens " + string.Join(", ", open) + (d.KeepOpen ? "" : " (closed after)"));
            if (d.DailyLimit > 0) lines.Add("\u2022  Daily limit " + HubSettings.Duration(d.DailyLimit).Replace("h 00m", "h"));
            checklist.Text = string.Join("\n", lines);
            checklist.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
