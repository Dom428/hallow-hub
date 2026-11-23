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
            Tuple.Create("2.20", new[] { "The Family page catches up: Hallow VPN offers Connect / Disconnect only when Windows won't ask and says why otherwise (NordLynx, or \"Windows login needed\"), and Connect fastest no longer changes the server you picked in Hallow VPN", "Hallow Tasks' live numbers, busy apps and \"Not responding\" (with a button to its Processes); Hallow Email says which account needs attention and why; Hallow Home shows Demo mode", "A Hallow Gear card: the lowest batteries, \"last known - 12 min ago\", \"waiting for its battery report\" and Refresh; Hallow Mixer's \"Spotify ad muted\" and ads muted today" }),
            Tuple.Create("2.19", new[] { "Keep the family running after updates: when a Hallow app that was running is updated, Hub starts it again in the tray and tells you once (\"Updated and restarted: Hallow Gear 1.3.1\") - held while you play. An app you closed yourself stays closed; Hallow Browser only gets a note", "Settings > Restart family apps after updates (on), with a chip per app to leave one out; the Family page has Start the apps I usually run", "Percentages are written without a space everywhere Hub shows them (45%, with no space before the % sign) - also in the menus and notes other Hallow apps hand to the Hallow icon" }),
            Tuple.Create("2.18", new[] { "Switch Steam accounts: the new Steam button (next to Wishlist) and the Hallow icon list the accounts Steam remembers on this PC - pick one and Hub closes Steam, signs you in as that account and opens Steam again", "Your Steam hours and last played follow the account you switch to, and so do sale alerts if they were set to the old account", "Sign in with another account opens Steam's own sign-in. Hub never sees or types a password, and won't switch while a game is running" }),
            Tuple.Create("2.17", new[] { "The whole family: the new Family button (next to Stats) shows every Hallow app - installed, running or not installed, its version and a live line (VPN connected, torrents downloading, unread mail, Shield blocked today...), counts and states only", "Quick actions on the cards - Connect fastest / Disconnect (Hallow VPN, IKEv2), Pause all / Resume all (Hallow Torrents), Open and Settings - and Install hands off to Hallow Setup", "The newer apps sit in the Hallow icon in a fixed order, and the ones you have but aren't running are under Start a Hallow app" }),
            Tuple.Create("2.16", new[] { "Light mode readability fixes: green and amber text, the accent as text and lilac labels are darker so they read on every light panel; a filled accent that would be hard to read (like violet) gets a deeper shade with white text", "Windows that are already open (game settings, emulators, What's new) switch light / dark and accent straight away" }),
            Tuple.Create("2.15", new[] { "Light and dark mode: Hub follows the Hallow family's Light / Dark / Same as Windows choice in Hallow Toolkit, live - or pick your own in Settings > Look (Follow Hallow, Light, Dark)" }),
            Tuple.Create("2.14", new[] { "A power plan while you play: Game settings > Power plan while it runs - for one game or all games, back to your own plan when the game closes", "More of the family under each game: Hallow Guard's score and weekly change, Hallow Shield's blocked count, and your PCs in Hallow Assist with Connect" }),
            Tuple.Create("2.13", new[] { "Hallow Guard and Hallow Assist get their own submenus in the Hallow icon (and hide their own tray icons) - every app shows its icon there now", "Hallow Guard's safety score sits next to CPU / RAM / GPU (click it to open Guard); Settings shows Guard's last scan and whether Assist is in a session" }),
            Tuple.Create("2.12", new[] { "Surprise me, your way: right-click the dice for From my backlog (Ctrl+Shift+R), a favorite, or one you haven't played in a month", "Links on every game: Steam pages, PCGamingWiki, HowLongToBeat and your own", "Close apps when a game starts (opened again after) - the When this game starts card lists everything that happens", "Stats: Your library - play status split, backlog, finished games and your ratings" }),
            Tuple.Create("2.11", new[] { "Hallow Keys (the new password manager) gets its own submenu in the Hallow icon - Lock now, Generate password, Open, Exit" }),
            Tuple.Create("2.10", new[] { "Play status (Playing, Beaten, Completed, On hold, Abandoned, Plan to play) and your own stars under Play - as filters and sorts", "Search finds genres, developers, notes and collections too - and genre:, dev:, status:, col:, note:, store: narrow it down" }),
            Tuple.Create("2.9", new[] { "Emulators and ROMs: Settings > Emulators... - add an emulator, its ROM folder and file types, and every ROM becomes a game", "ROMs get playtime, collections, notes, favourites and Big Picture like any other game (covers from a covers / boxart folder)" }),
            Tuple.Create("2.8", new[] { "About (the i button next to Play): description, genres, features, developer, release date and screenshots for Steam games", "Filters: by genre and feature (co-op, controller support...) - click a genre in About to see the others" }),
            Tuple.Create("2.7", new[] { "Settings > Back up library / Import library: your collections, notes, pins, names and pictures, settings and playtime in one zip for another PC", "Opt-in sale alerts for your public Steam wishlist - on sale, or below a price you set (the cart button)" }),
            Tuple.Create("2.6", new[] { "News and patch notes for Steam games: the newspaper button next to Play (Read more opens your browser)", "Stats: hours per week and per month, with your top games for each" }),
            Tuple.Create("2.5", new[] { "Uses the Hallow accent colour you pick in Hallow Toolkit (Settings > Use the Hallow accent); game glows keep their own colours", "If Hub ever closes unexpectedly, it keeps a crash report on this PC and tells you once - nothing is sent anywhere" }),
            Tuple.Create("2.4", new[] { "One Hallow icon: Hub's tray icon hosts the whole Hallow family - right-click it for each app's menu (Open, its actions, Exit), or Start the ones that aren't running", "The other Hallow apps hide their own tray icons while it's on (Settings > One Hallow icon)" }),
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
        static SolidColorBrush B(string hex) { return HallowLook.Brush(hex); }   // v2.16: remembers its palette colour (live restyle)
        public WhatsNewWindow(List<Tuple<string, string[]>> items)
        {
            Title = "What's new in Hallow Hub"; Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; HallowLook.Dress(this);
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
