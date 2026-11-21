// Hallow Hub v1.4 - Storage: every drive with its games, sizes, clean-up suggestions and uninstall through the launcher.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace HallowHub
{
    public partial class HubWindow
    {
        Border storagePanel; ToggleButton storageBtn; StackPanel storageBody; bool sizing, sized; Action afterSizing;

        // v2.1: folder sizes measured by Hub, kept across rescans (a rescan makes new Game objects)
        static readonly Dictionary<string, long> dirSizes = new Dictionary<string, long>();
        bool sizingCopies;
        void ApplyKnownSizes()
        {
            lock (dirSizes)
                foreach (var c in AllCopies().Where(x => x.SizeBytes <= 0 && !string.IsNullOrEmpty(x.InstallDir)))
                { long v; if (dirSizes.TryGetValue(c.InstallDir.ToLowerInvariant(), out v)) c.SizeBytes = v; }
        }
        // the copies of merged games get sized right away, so a copy you don't use can be offered for clean-up
        void SizeMergedCopies()
        {
            var todo = games.Where(g => g.Alts.Count > 0).SelectMany(g => new[] { g }.Concat(g.Alts))
                .Where(c => c.SizeBytes <= 0 && !string.IsNullOrEmpty(c.InstallDir) && Directory.Exists(c.InstallDir)).ToList();
            if (todo.Count == 0 || sizingCopies) return;
            Action<Game> measure = c => { var sz = Library.DirSize(c.InstallDir); c.SizeBytes = sz; lock (dirSizes) dirSizes[c.InstallDir.ToLowerInvariant()] = sz; };
            if (Snap.On) { foreach (var c in todo) measure(c); return; }
            sizingCopies = true;
            new Thread(() =>
            {
                foreach (var c in todo) { try { measure(c); } catch { } }
                Dispatcher.BeginInvoke(new Action(() => { sizingCopies = false; ApplyKnownSizes(); ShowHero(); }));
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }

        // not played for 60 days (or never, when it's on the PC)
        bool Stale(Game g)
        {
            if (S.For(g.Id).Hidden || IsRunning(g)) return false;
            var last = LastOf(g);
            return last == null ? Total(g) < 1 : (DateTime.UtcNow - last.Value).TotalDays > 60;
        }

        Border BuildStorage()
        {
            var b = new Border { Background = B("#211B2A"), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24, 16, 12, 18), Width = 820, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 62, 0, 24), Visibility = Visibility.Collapsed };
            b.Effect = HallowLook.Shadow(30, 0.6, 4);
            var sp = new DockPanel();
            var sh = new DockPanel { Margin = new Thickness(0, 0, 12, 14) };
            var close = new Button { Content = "x", Tag = "icon", Style = (Style)FindResource("Btn"), Width = 30, Height = 28, Padding = new Thickness(0), ToolTip = "Close" };
            close.Click += (s, e) => storageBtn.IsChecked = false;
            DockPanel.SetDock(close, Dock.Right); sh.Children.Add(close);
            var ci = Icons.Make("hard-drive", 16, B("#F07820")); ci.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(ci, Dock.Left); sh.Children.Add(ci);
            sh.Children.Add(new TextBlock { Text = "Storage", FontWeight = FontWeights.Bold, FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(sh, Dock.Top); sp.Children.Add(sh);
            storageBody = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            sp.Children.Add(new ScrollViewer { Content = storageBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 600 });
            b.Child = sp; return b;
        }

        void FillStorage()
        {
            storageBody.Children.Clear();
            var copies = AllCopies().Where(c => !string.IsNullOrEmpty(c.InstallDir) && Directory.Exists(c.InstallDir)).ToList();
            if (Snap.On && !sized) { foreach (var c in copies.Where(x => x.SizeBytes <= 0)) c.SizeBytes = Library.DirSize(c.InstallDir); sized = true; }
            // sizes the launcher doesn't tell us: measured once in the background
            if (!sized && !sizing && copies.Any(c => c.SizeBytes <= 0))
            {
                sizing = true;
                var todo = copies.Where(c => c.SizeBytes <= 0).ToList();
                new Thread(() =>
                {
                    foreach (var c in todo) { var sz = Library.DirSize(c.InstallDir); c.SizeBytes = sz; lock (dirSizes) dirSizes[c.InstallDir.ToLowerInvariant()] = sz; }
                    Dispatcher.BeginInvoke(new Action(() => { sizing = false; sized = true; if (storagePanel.Visibility == Visibility.Visible) FillStorage(); RebuildGrid(); if (afterSizing != null) afterSizing(); }));
                }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
            }
            if (sizing) storageBody.Children.Add(new TextBlock { Text = "Measuring games that their launcher doesn't size...", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 10), FontSize = 11.5 });

            // ---- clean-up suggestions: older duplicate copies, and games not played for a while
            var tips = CleanupTips();
            if (tips.Count > 0)
            {
                var box = new Border { Background = B("#2A2333"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 8), Margin = new Thickness(0, 0, 0, 18),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(90, CI("#F0BE5A").R, CI("#F0BE5A").G, CI("#F0BE5A").B)), BorderThickness = new Thickness(1) };
                var bs = new StackPanel();
                var hd = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                var bi = Icons.Make("broom", 13, BI("#F0BE5A")); bi.Margin = new Thickness(0, 0, 9, 0); hd.Children.Add(bi);
                hd.Children.Add(new TextBlock { Text = "You could free up " + Size(tips.Sum(t => t.Item1.SizeBytes)), FontWeight = FontWeights.Bold, FontSize = 14, Foreground = B("#F0BE5A") });
                bs.Children.Add(hd);
                foreach (var t in tips.Take(6)) bs.Children.Add(GameRow(t.Item1, t.Item2, true));
                box.Child = bs; storageBody.Children.Add(box);
            }

            // ---- every drive that has games on it
            foreach (var grp in copies.GroupBy(c => (c.Drive ?? Path.GetPathRoot(c.InstallDir).TrimEnd('\\')).ToUpperInvariant()).OrderBy(x => x.Key))
            {
                DriveInfo di = null; try { di = new DriveInfo(grp.Key); if (!di.IsReady) di = null; } catch { }
                long onDrive = grp.Sum(c => Math.Max(0, c.SizeBytes));
                var head = new DockPanel { Margin = new Thickness(0, 4, 0, 6) };
                var di2 = Icons.Make("hard-drive", 13, B("#A98BE0")); di2.Margin = new Thickness(0, 0, 9, 0); DockPanel.SetDock(di2, Dock.Left); head.Children.Add(di2);
                var free = new TextBlock { Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center,
                    Text = di == null ? "" : Size(di.AvailableFreeSpace) + " free of " + Size(di.TotalSize) };
                DockPanel.SetDock(free, Dock.Right); head.Children.Add(free);
                head.Children.Add(new TextBlock { Text = grp.Key + (di != null && di.VolumeLabel.Length > 0 ? "  " + di.VolumeLabel : "") + "   ·   " + grp.Count() + (grp.Count() == 1 ? " game, " : " games, ") + Size(onDrive),
                    FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                storageBody.Children.Add(head);
                if (di != null && di.TotalSize > 0)
                {
                    // bar: games (orange) | everything else (grey) | free (dark)
                    double t = di.TotalSize, gFrac = Math.Min(1, onDrive / t), uFrac = Math.Max(0, Math.Min(1 - gFrac, (di.TotalSize - di.AvailableFreeSpace - onDrive) / t));
                    var bar = new Grid { Height = 12, Margin = new Thickness(0, 0, 0, 4) };
                    bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gFrac, GridUnitType.Star) });
                    bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(uFrac, GridUnitType.Star) });
                    bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - gFrac - uFrac), GridUnitType.Star) });
                    var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                    fill.GradientStops.Add(new GradientStop(C("#FF9A45"), 0)); fill.GradientStops.Add(new GradientStop(C("#F05A8C"), 1));
                    var g1 = new Border { Background = fill, CornerRadius = new CornerRadius(6, 0, 0, 6), ToolTip = "Games: " + Size(onDrive) };
                    var g2 = new Border { Background = B("#5A5268"), ToolTip = "Everything else" };
                    var g3 = new Border { Background = B("#2E2639"), CornerRadius = new CornerRadius(0, 6, 6, 0), ToolTip = "Free: " + Size(di.AvailableFreeSpace) };
                    Grid.SetColumn(g2, 1); Grid.SetColumn(g3, 2); bar.Children.Add(g1); bar.Children.Add(g2); bar.Children.Add(g3);
                    storageBody.Children.Add(bar);
                    var legend = new TextBlock { Foreground = (Brush)FindResource("Muted"), FontSize = 10.5, Margin = new Thickness(0, 0, 0, 8), Text = "orange = games   ·   grey = everything else   ·   " + (HallowLook.Light ? "light" : "dark") + " = free" };
                    storageBody.Children.Add(legend);
                }
                foreach (var c in grp.OrderByDescending(x => x.SizeBytes)) storageBody.Children.Add(GameRow(c, null, false));
                storageBody.Children.Add(new Border { Height = 14 });
            }
            if (copies.Count == 0) storageBody.Children.Add(new TextBlock { Text = "No installed games found.", Foreground = (Brush)FindResource("Muted") });
        }

        // one game copy: cover thumb, name, size, last played, open folder, uninstall
        FrameworkElement GameRow(Game c, string why, bool compact)
        {
            var main = Main(c);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, compact ? 6 : 4), Background = Brushes.Transparent };
            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var open = new Button { Content = "Open folder", Tag = "icon", Width = 30, Height = 28, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Open its folder" };
            open.Click += (s, e) => OpenFolder(c);
            var un = new Button { Content = "Remove", Tag = "icon", Width = 30, Height = 28, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Uninstall through " + c.SourceName };
            un.Click += (s, e) => Uninstall(c);
            btns.Children.Add(open); btns.Children.Add(un);
            DockPanel.SetDock(btns, Dock.Right); row.Children.Add(btns);
            var size = new TextBlock { Text = c.SizeBytes > 0 ? Size(c.SizeBytes) : (sizing ? "..." : "?"), Width = 70, TextAlignment = TextAlignment.Right, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(size, Dock.Right); row.Children.Add(size);
            var thumb = Img(main.Header, 160);
            var th = new Border { Width = 64, Height = 30, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 10, 0), Background = thumb != null ? (Brush)new ImageBrush(thumb) { Stretch = Stretch.UniformToFill } : B("#2E2639") };
            DockPanel.SetDock(th, Dock.Left); row.Children.Add(th);
            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            txt.Children.Add(new TextBlock { Text = main.Name + (main.Alts.Count > 0 ? "  (" + c.SourceName + ")" : ""), TextTrimming = TextTrimming.CharacterEllipsis });
            txt.Children.Add(new TextBlock { Text = why ?? ((c.NeedsUpdate ? "update waiting   ·   " : "") + (RowLast(c) == null ? "no playtime recorded" : "last played " + AgoLow(RowLast(c)))), Foreground = (Brush)FindResource("Muted"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(txt);
            row.MouseLeftButtonUp += (s, e) => { if (e.OriginalSource is TextBlock || e.OriginalSource is Border) Select(main); };
            return row;
        }

        // a merged game's copies each show their own last play
        DateTime? RowLast(Game c) { var m = Main(c); return m.Alts.Count > 0 ? CopyLast(c) : LastOf(m); }
        static string AgoLow(DateTime? d) { var s = HubSettings.Ago(d); return char.IsDigit(s[0]) ? s : s.ToLowerInvariant(); }

        // clean-up suggestions: a second copy you don't play, and games not played for a while (biggest first, 256 MB and up)
        List<Tuple<Game, string>> CleanupTips()
        {
            var tips = new List<Tuple<Game, string>>();
            foreach (var g in games.Where(x => x.Alts.Count > 0))
                foreach (var a in new[] { g }.Concat(g.Alts))
                {
                    if (a == PlayCopy(g) || tracker.Now.ContainsKey(a.Id)) continue;
                    var al = CopyLast(a);
                    tips.Add(Tuple.Create(a, "a second copy of " + g.Name + (a.Drive != null ? " on " + a.Drive : "") + " - " + (al == null ? "not played here that Hub knows of" : "last played here " + AgoLow(al)) + "; you play the " + PlayCopy(g).SourceName + " one"));
                }
            foreach (var g in games.Where(Stale))
            {
                var last = LastOf(g);
                tips.Add(Tuple.Create(PlayCopy(g), last == null ? "no playtime recorded" : "last played " + HubSettings.Ago(last).ToLowerInvariant()));
            }
            return tips.Where(t => t.Item1.SizeBytes >= (256L << 20)).OrderByDescending(t => t.Item1.SizeBytes).ToList();
        }

        // uninstalling always goes through the game's own launcher, which asks again before it deletes anything
        static string UninstallHow(Game c)
        {
            return c.Source == "steam" ? "Steam opens its own uninstall box - confirm there to remove it." :
                c.Source == "battlenet" ? "Battle.net's uninstaller opens - follow it to remove this copy." :
                c.Source == "epic" ? "Epic Games doesn't let other apps uninstall. Hub opens your Epic library so you can do it there." :
                "Hub only removes it from your library and opens its folder - delete the folder yourself if you want the space back.";
        }
        string UninstallTitle(Game c) { var main = Main(c); return main.Name + (main.Alts.Count > 0 ? " (" + c.SourceName + " copy)" : "") + (c.SizeBytes > 0 ? " - " + Size(c.SizeBytes) : ""); }
        void Uninstall(Game c)
        {
            var main = Main(c);
            var r = MessageBox.Show(this, "Uninstall " + UninstallTitle(c) + "?\n\n" + UninstallHow(c) + "\n\nYour playtime in Hub is kept.",
                "Uninstall " + main.Name, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            StartUninstall(c);
        }
        // after you said yes (here, or in Big Picture): hand it to the launcher
        void StartUninstall(Game c)
        {
            var main = Main(c);
            try
            {
                if (c.Source == "steam") Process.Start(new ProcessStartInfo("steam://uninstall/" + c.Id.Substring(6)) { UseShellExecute = true });
                else if (c.Source == "battlenet")
                {
                    var u = (c.Uninstall ?? "").Trim(); string exe, args;
                    if (u.StartsWith("\"")) { int q = u.IndexOf('"', 1); exe = u.Substring(1, q - 1); args = u.Substring(q + 1).Trim(); }
                    else { int x = u.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); exe = x > 0 ? u.Substring(0, x + 4) : u; args = x > 0 ? u.Substring(x + 4).Trim() : ""; }
                    if (exe.Length > 0 && File.Exists(exe)) Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
                    else { Status("Couldn't find Battle.net's uninstaller - uninstall " + main.Name + " in Battle.net (gear next to Play > Uninstall)."); return; }
                }
                else if (c.Source == "epic") Process.Start(new ProcessStartInfo("com.epicgames.launcher://library") { UseShellExecute = true });
                else { S.Custom.RemoveAll(x => x.Id == c.Id); S.Save(); OpenFolder(c); Rescan(); }
                Status("Uninstall of " + main.Name + " started in " + c.SourceName + " - your library updates by itself when it's done.");
            }
            catch (Exception ex) { Status("Couldn't start the uninstall: " + ex.Message); }
        }
    }
}