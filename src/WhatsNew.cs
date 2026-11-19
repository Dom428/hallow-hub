// Hallow Hub - "What's new": shown once after an update, with the versions you skipped.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HallowHub
{
    public static class WhatsNew
    {
        // Newest first. One or two short lines per version.
        public static readonly Tuple<string, string[]>[] Notes = {
            Tuple.Create("2.3", new[] { "Filters (next to the sort): store, size, last played and playtime, all together; hide games with the eye button - the Hidden filter brings them back", "Big Picture: your collections as filters (LB / RB), and Storage with the controller (Menu)" }),
            Tuple.Create("2.2", new[] { "Hallow family on every game page: Saves (Hallow Vault), Clips (Hallow Clips) and Overlay on/off (Hallow Overlay) - only for the apps you have", "Break reminders and the daily limit show on Hallow Overlay when it's running" }),
            Tuple.Create("2.1", new[] { "Games in two stores (like Overwatch) start the copy you actually play, using Steam's and Battle.net's own last-played", "A copy you haven't used for a month is offered as a clean-up (uninstall through its launcher, which asks first)" }),
            Tuple.Create("2.0", new[] { "Big Picture (TV button, F11, or View on a controller): full screen, made for the couch", "Click the tray icon for a quick launcher with your recent games (double-click opens Hub)" }),
            Tuple.Create("1.9", new[] { "Your PC right now: CPU, free RAM, GPU temperature and free space, next to Play", "Start your recent games from your phone (Hallow Toolkit 5.3 dashboard)" }),
            Tuple.Create("1.8", new[] { "Weekly recap on Mondays and play streaks; Stats > Export to a spreadsheet", "Opt-in: post to Discord when you start a game (Settings, uses Hallow Toolkit's Discord link)" }),
            Tuple.Create("1.7", new[] { "Cover size (Small / Medium / Large) and a list view", "Notes per game, pin games to the top, and a slowly moving banner" }),
            Tuple.Create("1.6", new[] { "Game settings: apps that open with a game (Discord, OBS...) and close after, and a daily limit", "Settings: break reminder; Stats: your recent sessions" }),
            Tuple.Create("1.5", new[] { "Finds EA app, Ubisoft Connect, GOG, Riot and Xbox games too", "Game settings > Look: your own name, cover and banner for any game" }),
            Tuple.Create("1.4", new[] { "Storage page (drive icon): what each drive holds and what you could free up", "Uninstall through the game's own launcher; Biggest first; Needs update and Not played lately filters" }),
            Tuple.Create("1.3", new[] { "Updates itself from GitHub (asks first) - and this window", "New installs, uninstalls and finished Steam updates show up by themselves", "Steam download progress on the card (Updating 42%)", "Right-click a game > Create desktop shortcut - starts it through Hub with your preset and session" }),
            Tuple.Create("1.2", new[] { "Game settings: launch options, run as admin, collections", "Surprise me (dice), keyboard shortcuts and Xbox controller" }),
            Tuple.Create("1.1", new[] { "Overwatch is one card (Play on Steam / Battle.net)", "Your Steam hours, update badges, Continue playing, Stats" }),
        };

        static int Cmp(string a, string b) { Version x, y; return Version.TryParse(a, out x) && Version.TryParse(b, out y) ? x.CompareTo(y) : string.CompareOrdinal(a, b); }

        // The versions newer than `seen` (at most 6). Empty for a fresh install or no update.
        public static List<Tuple<string, string[]>> Since(string seen)
        {
            if (string.IsNullOrEmpty(seen)) return new List<Tuple<string, string[]>>();
            return Notes.Where(n => Cmp(n.Item1, seen) > 0 && Cmp(n.Item1, Program.Version) <= 0).Take(6).ToList();
        }
    }

    public class WhatsNewWindow : Window
    {
        static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        public WhatsNewWindow(List<Tuple<string, string[]>> items)
        {
            Title = "What's new in Hallow Hub"; Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text");
            FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
            sp.Children.Add(new TextBlock { Text = "What's new in Hallow Hub " + Program.Version, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = B("#F07820"), Margin = new Thickness(0, 0, 0, 12) });
            foreach (var v in items)
            {
                sp.Children.Add(new TextBlock { Text = v.Item1, FontWeight = FontWeights.Bold, Foreground = B("#A98BE0"), Margin = new Thickness(0, 6, 0, 3) });
                foreach (var line in v.Item2)
                {
                    var d = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                    var dot = new TextBlock { Text = "\u2022", Foreground = B("#F07820"), Width = 16, FontWeight = FontWeights.Bold }; DockPanel.SetDock(dot, Dock.Left);
                    d.Children.Add(dot); d.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
                    sp.Children.Add(d);
                }
            }
            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var all = new Button { Content = "All release notes", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0) };
            all.Click += (s, e) => { try { Process.Start(new ProcessStartInfo("https://github.com/Dom428/hallow-hub/releases") { UseShellExecute = true }); } catch { } };
            var ok = new Button { Content = "Got it", Style = (Style)FindResource("Accent"), MinWidth = 90 };
            ok.Click += (s, e) => Close();
            foot.Children.Add(all); foot.Children.Add(ok); sp.Children.Add(foot);
            Content = sp;
        }
    }
}
