// Hallow Hub v2.5 - the window's side of the Hallow accent (live, when Hallow Toolkit changes it) and the crash notice.
// v2.15: light / dark mode goes the same way (theme.json "mode", Hub's own Look setting, Windows' app mode for "system").
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowHub
{
    public partial class HubWindow
    {
        FileSystemWatcher themeWatch; DispatcherTimer themeSoon, systemMode;

        // watch %APPDATA%\Hallow for theme.json (the folder may only appear once Toolkit writes it - checked again every 15 s)
        void WatchTheme()
        {
            if (Snap.On || themeWatch != null) return;
            if (themeSoon == null)
            {
                themeSoon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                themeSoon.Tick += (s, e) => { themeSoon.Stop(); ApplyAccent(false); };
            }
            if (systemMode == null)
            {
                // "Same as Windows": follow Windows' app mode live (a registry read every 3 s, only while it matters)
                systemMode = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                systemMode.Tick += (s, e) => { if (HallowLook.FollowsSystem && (HallowLook.SystemLight() == true) != HallowLook.Light) ApplyAccent(false); };
                systemMode.Start();
            }
            try
            {
                if (!Directory.Exists(HallowLook.Dir)) return;
                var w = new FileSystemWatcher(HallowLook.Dir, "theme.json*") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                FileSystemEventHandler h = (o, e) => Dispatcher.BeginInvoke(new Action(() => { themeSoon.Stop(); themeSoon.Start(); }));
                w.Changed += h; w.Created += h; w.Deleted += h; w.Renamed += (o, e) => h(o, e);
                w.EnableRaisingEvents = true; themeWatch = w;
                ApplyAccent(false);   // it may have changed while nobody watched
            }
            catch { }
        }

        // the accent changed: new theme, and the window rebuilt with it (game glows keep their own colours)
        void ApplyAccent(bool force)
        {
            try
            {
                if (!HallowLook.Refresh() && !force) return;
                Program.LoadTheme(Application.Current);
                accentCache.Clear();
                bool settingsOpen = gearBtn != null && gearBtn.IsChecked == true;
                Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text"); HallowLook.TitleBar(this);   // v2.15
                if (notesPop != null) notesPop.IsOpen = false; if (linksPop != null) linksPop.IsOpen = false; notesPop = null; linksPop = null;   // rebuilt with the new colours
                Content = Build();
                RebuildChips(); RebuildGrid(); ShowHero(); ShowPc(); CheckLinks();
                familySig = null; ShowFamily(true); statusUntil = DateTime.MinValue; UpdateStatus();
                if (settingsOpen) gearBtn.IsChecked = true;
            }
            catch { }
        }

        // ---- "Hallow Hub closed unexpectedly last time" - once per crash report
        void ShowCrashNotice()
        {
            if (Snap.On || !IsVisible) return;
            var f = Crash.Newest(); if (f == null) return;
            var name = Path.GetFileName(f);
            if (S.LastCrashShown == name) return;
            S.LastCrashShown = name; S.Save();
            new CrashWindow(f) { Owner = this }.Show();
        }
        static string LastCrashIso() { var w = Crash.When(Crash.Newest()); return w == null ? null : w.Value.ToString("yyyy-MM-ddTHH:mm:ss"); }
    }

    public class CrashWindow : Window
    {
        static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(HallowLook.Hex(hex))); }
        public CrashWindow(string file)
        {
            Title = "Hallow Hub"; Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; HallowLook.Dress(this);
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text");
            FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var ic = Icons.Make("heart-pulse", 18, B("#F07820")); ic.Margin = new Thickness(0, 0, 10, 0); head.Children.Add(ic);
            head.Children.Add(new TextBlock { Text = "Hallow Hub closed unexpectedly last time", FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(head);
            var when = Crash.When(file);
            sp.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), LineHeight = 19,
                Text = (when != null ? "On " + when.Value.ToString("dddd d MMM 'at' HH:mm") + ". " : "") + "Hub wrote down what went wrong in a small text file on this PC. Nothing was sent anywhere - your playtime and settings are fine." });
            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var show = new Button { Content = "Show details", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0) };
            show.Click += (s, e) => { try { Process.Start("notepad.exe", "\"" + file + "\""); } catch { } };
            var open = new Button { Content = "Open folder", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0) };
            open.Click += (s, e) => { try { Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch { } };
            var ok = new Button { Content = "OK", Style = (Style)FindResource("Accent"), MinWidth = 80 };
            ok.Click += (s, e) => Close();
            foot.Children.Add(show); foot.Children.Add(open); foot.Children.Add(ok);
            sp.Children.Add(foot);
            Content = sp;
        }
    }
}
