// Hallow Hub v1.2 - one game's settings: launch options, run as administrator, and which collections it's in.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace HallowHub
{
    public class GameSettingsWindow : Window
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }

        public GameSettingsWindow(Game g, GameData d, HubSettings s, bool exeLaunch)
        {
            Title = g.Name + " - settings"; Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text"); FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
            SourceInitialized += (o, e) => { int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, 4); };
            var sp = new StackPanel { Margin = new Thickness(24, 18, 24, 20) };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            var hi = Icons.Make("sliders", 16, B("#F07820")); hi.Margin = new Thickness(0, 0, 10, 0); head.Children.Add(hi);
            head.Children.Add(new TextBlock { Text = g.Name, FontSize = 17, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 380 });
            sp.Children.Add(head);

            Func<string, string, TextBlock> label = (text, icon) => new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
            // v1.5: your own name and pictures
            sp.Children.Add(label("Name", "pen"));
            var nameBox = new TextBox { Style = (Style)FindResource("Field"), Text = string.IsNullOrEmpty(d.CustomName) ? (g.OriginalName ?? g.Name) : d.CustomName };
            sp.Children.Add(nameBox);
            var pics = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
            string cover = d.CustomCover ?? "", hero = d.CustomHero ?? "";
            var picNote = new TextBlock { Foreground = (Brush)FindResource("Muted"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
            Action note = () => picNote.Text = (cover != "" ? "Cover: your picture. " : "Cover: from " + g.SourceName + ". ") + (hero != "" ? "Banner: your picture." : "Banner: from " + g.SourceName + ".") + "  Covers look best tall (600 x 900), banners wide (1920 x 620).";
            Func<string, string> pick = what =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Pick a " + what + " picture", Filter = "Pictures (*.jpg; *.jpeg; *.png; *.webp; *.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp" };
                if (dlg.ShowDialog(this) != true) return null;
                try
                {
                    var dir = System.IO.Path.Combine(HubSettings.Dir, "art"); System.IO.Directory.CreateDirectory(dir);
                    var safe = new string(g.Id.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
                    var dest = System.IO.Path.Combine(dir, safe + "-" + what + "-" + DateTime.Now.Ticks + System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant());
                    System.IO.File.Copy(dlg.FileName, dest, true); return dest;   // a copy, so moving the original doesn't break it
                }
                catch { return null; }
            };
            var coverBtn = new Button { Content = "Cover picture...", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 6, 6) };
            coverBtn.Click += (o, e) => { var f = pick("cover"); if (f != null) { cover = f; note(); } };
            var heroBtn = new Button { Content = "Banner picture...", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 6, 6) };
            heroBtn.Click += (o, e) => { var f = pick("banner"); if (f != null) { hero = f; note(); } };
            var resetBtn = new Button { Content = "Reset look", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 6, 6), ToolTip = "Back to the launcher's name and pictures" };
            resetBtn.Click += (o, e) => { cover = ""; hero = ""; nameBox.Text = g.OriginalName ?? g.Name; note(); };
            pics.Children.Add(coverBtn); pics.Children.Add(heroBtn); pics.Children.Add(resetBtn);
            sp.Children.Add(pics); note(); sp.Children.Add(picNote);
            Closing += (o, e) =>
            {
                if (DialogResult != true) return;
                var n = (nameBox.Text ?? "").Trim();
                d.CustomName = n.Length == 0 || n == (g.OriginalName ?? g.Name) ? "" : n;
                d.CustomCover = cover; d.CustomHero = hero;
            };
            // launch options
            sp.Children.Add(label("Launch options", "terminal"));
            var args = new TextBox { Style = (Style)FindResource("Field"), Text = d.LaunchArgs ?? "" };
            sp.Children.Add(args);
            bool argsWork = g.Source == "steam" || exeLaunch;
            sp.Children.Add(new TextBlock { Text = !argsWork ? "This launcher doesn't take launch options from other apps - set them in " + g.SourceName + "." :
                g.Source == "steam" ? "Passed to the game through Steam, on top of its own Launch Options. Example: -novid -high" : "Passed to the game's .exe when Hub starts it.",
                Foreground = (Brush)FindResource("Muted"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 14) });
            args.IsEnabled = argsWork;
            // admin
            var admin = new CheckBox { Content = new TextBlock { Text = "Run as administrator", TextWrapping = TextWrapping.Wrap }, Style = (Style)FindResource("Switch"), IsChecked = d.Admin, IsEnabled = exeLaunch };
            sp.Children.Add(admin);
            sp.Children.Add(new TextBlock { Text = exeLaunch ? "Windows asks you to allow it each time (only some old games need this)." : "Only for games started from their .exe - Steam, Battle.net and Epic start games themselves.",
                Foreground = (Brush)FindResource("Muted"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(52, 0, 0, 14) });
            // collections
            sp.Children.Add(label("Collections", "tags"));
            var tags = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            var chosen = new HashSet<string>(d.Tags ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var all = new List<string>(s.Collections);
            Action fill = null;
            fill = () =>
            {
                tags.Children.Clear();
                foreach (var c in all)
                {
                    var name = c;
                    var chip = new ToggleButton { Content = name, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 6, 6), IsChecked = chosen.Contains(name), Padding = new Thickness(12, 5, 12, 5) };
                    chip.Click += (o, e) => { if (chip.IsChecked == true) chosen.Add(name); else chosen.Remove(name); };
                    tags.Children.Add(chip);
                }
                if (all.Count == 0) tags.Children.Add(new TextBlock { Text = "No collections yet - make one below (like \"With friends\" or \"Shooters\").", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
            };
            fill();
            sp.Children.Add(tags);
            var newRow = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
            var addBtn = new Button { Content = "Add", Style = (Style)FindResource("Btn"), Margin = new Thickness(8, 0, 0, 0) };
            var newName = new TextBox { Style = (Style)FindResource("Field") };
            Action add = () =>
            {
                var n = (newName.Text ?? "").Trim(); if (n.Length == 0) return;
                if (n.Length > 24) n = n.Substring(0, 24);
                var existing = all.FirstOrDefault(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
                if (existing == null) { all.Add(n); existing = n; }
                chosen.Add(existing); newName.Text = ""; fill();
            };
            addBtn.Click += (o, e) => add();
            newName.KeyDown += (o, e) => { if (e.Key == Key.Enter) { add(); e.Handled = true; } };
            DockPanel.SetDock(addBtn, Dock.Right); newRow.Children.Add(addBtn); newRow.Children.Add(newName);
            sp.Children.Add(new TextBlock { Text = "New collection", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, Margin = new Thickness(0, 0, 0, 4) });
            sp.Children.Add(newRow);
            // v1.6: apps that open with this game, and a daily limit
            sp.Children.Add(label("Open with this game", "rocket"));
            var apps = new List<string>(d.OpenWith ?? new List<string>());
            var appList = new StackPanel();
            Action fillApps = null;
            fillApps = () =>
            {
                appList.Children.Clear();
                foreach (var a in apps.ToList())
                {
                    var exe = a; var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                    var rm = new Button { Content = "x", Tag = "icon", Width = 26, Height = 24, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), ToolTip = "Don't open it any more" };
                    rm.Click += (o, e) => { apps.Remove(exe); fillApps(); };
                    DockPanel.SetDock(rm, Dock.Right); row.Children.Add(rm);
                    string nm; try { nm = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileDescription; } catch { nm = null; }
                    if (string.IsNullOrWhiteSpace(nm)) nm = System.IO.Path.GetFileNameWithoutExtension(exe);
                    row.Children.Add(new TextBlock { Text = nm.Trim(), VerticalAlignment = VerticalAlignment.Center, ToolTip = exe, TextTrimming = TextTrimming.CharacterEllipsis });
                    appList.Children.Add(row);
                }
                if (apps.Count == 0) appList.Children.Add(new TextBlock { Text = "Nothing yet - for example Discord, OBS or Medal.", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5 });
            };
            fillApps(); sp.Children.Add(appList);
            var addApp = new Button { Content = "Add app...", Style = (Style)FindResource("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 6) };
            addApp.Click += (o, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Pick an app to open with " + g.Name, Filter = "Programs (*.exe)|*.exe" };
                if (dlg.ShowDialog(this) == true && !apps.Contains(dlg.FileName, StringComparer.OrdinalIgnoreCase)) { apps.Add(dlg.FileName); fillApps(); }
            };
            sp.Children.Add(addApp);
            var keep = new CheckBox { Content = new TextBlock { Text = "Leave them open when the game closes", TextWrapping = TextWrapping.Wrap }, Style = (Style)FindResource("Switch"), IsChecked = d.KeepOpen };
            sp.Children.Add(keep);
            sp.Children.Add(new TextBlock { Text = "Apps that were already running are never closed. Closing is like clicking X, so they can still ask to save.", Foreground = (Brush)FindResource("Muted"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(52, 0, 0, 14) });
            // v2.12: apps to close when the game starts (a launch checklist), opened again after
            sp.Children.Add(label("Close when this game starts", "power-off"));
            var shut = new List<string>((d.CloseFirst ?? new List<string>()).Where(CloseApps.Allowed));
            var shutList = new StackPanel();
            Action fillShut = null;
            fillShut = () =>
            {
                shutList.Children.Clear();
                foreach (var a in shut.ToList())
                {
                    var exe = a; var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                    var rm = new Button { Content = "x", Tag = "icon", Width = 26, Height = 24, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), ToolTip = "Don't close it any more" };
                    rm.Click += (o, e) => { shut.Remove(exe); fillShut(); };
                    DockPanel.SetDock(rm, Dock.Right); row.Children.Add(rm);
                    row.Children.Add(new TextBlock { Text = CloseApps.Name(exe), VerticalAlignment = VerticalAlignment.Center, ToolTip = exe, TextTrimming = TextTrimming.CharacterEllipsis });
                    shutList.Children.Add(row);
                }
                if (shut.Count == 0) shutList.Children.Add(new TextBlock { Text = "Nothing yet - for example a browser, a sync app or a video editor that slows games down.", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
            };
            fillShut(); sp.Children.Add(shutList);
            var shutBtns = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
            var addRunning = new Button { Content = "Add a running app", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Pick from the apps open right now" };
            addRunning.Click += (o, e) =>
            {
                var m = new ContextMenu { PlacementTarget = addRunning, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                foreach (var r in CloseApps.Running())
                {
                    var exe = r.Key; var mi = new MenuItem { Header = r.Value, ToolTip = exe, IsEnabled = !shut.Contains(exe, StringComparer.OrdinalIgnoreCase) };
                    mi.Click += (o2, e2) => { shut.Add(exe); fillShut(); };
                    m.Items.Add(mi);
                }
                if (m.Items.Count == 0) m.Items.Add(new MenuItem { Header = "No other apps with a window are open", IsEnabled = false });
                m.IsOpen = true;
            };
            var addShut = new Button { Content = "Add app...", Style = (Style)FindResource("Btn") };
            addShut.Click += (o, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Pick an app to close when " + g.Name + " starts", Filter = "Programs (*.exe)|*.exe" };
                if (dlg.ShowDialog(this) != true || shut.Contains(dlg.FileName, StringComparer.OrdinalIgnoreCase)) return;
                if (!CloseApps.Allowed(dlg.FileName)) { MessageBox.Show(this, CloseApps.Name(dlg.FileName) + " can't be closed by Hub - it's part of Windows, a game launcher or a Hallow app.", "Hallow Hub"); return; }
                shut.Add(dlg.FileName); fillShut();
            };
            shutBtns.Children.Add(addRunning); shutBtns.Children.Add(addShut); sp.Children.Add(shutBtns);
            var reopen = new CheckBox { Content = new TextBlock { Text = "Open them again when the game closes", TextWrapping = TextWrapping.Wrap }, Style = (Style)FindResource("Switch"), IsChecked = !d.NoReopen };
            sp.Children.Add(reopen);
            sp.Children.Add(new TextBlock { Text = "Like clicking X - an app can still ask to save, and nothing is force-closed. Windows, game launchers and the Hallow apps are never closed. For High performance power and your Boost apps, turn on the Hallow Toolkit game session.", Foreground = (Brush)FindResource("Muted"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(52, 0, 0, 14) });
            Closing += (o, e) => { if (DialogResult == true) { d.CloseFirst = shut; d.NoReopen = reopen.IsChecked != true; } };
            var lim =new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            var limBox = new ComboBox { Style = (Style)FindResource("Combo"), Width = 150 };
            foreach (var m in new[] { 0, 30, 60, 90, 120, 180, 240 }) limBox.Items.Add(new ComboBoxItem { Content = m == 0 ? "No limit" : HubSettings.Duration(m).Replace("h 00m", "h") + " a day", Tag = m });
            limBox.SelectedItem = limBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == d.DailyLimit) ?? limBox.Items[0];
            DockPanel.SetDock(limBox, Dock.Right); lim.Children.Add(limBox);
            lim.Children.Add(new TextBlock { Text = "Daily limit (a reminder, nothing is closed)", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(lim);
            Closing += (o, e) =>
            {
                if (DialogResult != true) return;
                d.OpenWith = apps; d.KeepOpen = keep.IsChecked == true;
                var it = limBox.SelectedItem as ComboBoxItem; d.DailyLimit = it == null ? 0 : (int)it.Tag;
            };
            // buttons
            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "Cancel", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            var save = new Button { Content = "Save", Style = (Style)FindResource("Accent"), MinWidth = 96, IsDefault = true };
            save.Click += (o, e) =>
            {
                if (newName.Text.Trim().Length > 0) add();
                d.LaunchArgs = (args.Text ?? "").Trim(); d.Admin = admin.IsChecked == true && exeLaunch;
                d.Tags = all.Where(chosen.Contains).ToList();
                s.Collections = all;
                DialogResult = true;
            };
            foot.Children.Add(cancel); foot.Children.Add(save); sp.Children.Add(foot);
            Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = SystemParameters.WorkArea.Height - 60 };
            Loaded += (o, e) => args.Focus();
        }
    }
}
