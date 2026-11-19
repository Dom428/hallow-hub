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
            Content = sp;
            Loaded += (o, e) => args.Focus();
        }
    }
}
