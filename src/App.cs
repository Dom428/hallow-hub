// Hallow Hub - all your games in one place. Play starts the game; while it runs, Hub can switch your Hallow Mixer preset
// and turn on a Hallow Toolkit game session, and it counts your playtime. WPF built in code (C# 5 / .NET Framework 4.8).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;

[assembly: AssemblyTitle("Hallow Hub")]
[assembly: AssemblyProduct("Hallow Hub")]
[assembly: AssemblyDescription("All your games in one place, with playtime, Hallow Mixer presets and Hallow Toolkit game sessions")]
[assembly: AssemblyVersion("2.29.0.0")]
[assembly: AssemblyFileVersion("2.29.0.0")]

namespace HallowHub
{
    public static class Program
    {
        public const string Version = "2.29";
        [STAThread]
        public static void Main(string[] args)
        {
            int ci = Array.IndexOf(args, "--cmd");   // v2.2: Hallow family link - send one line to the running Hub, print the reply
            if (ci >= 0) { Environment.Exit(Cmd(ci + 1 < args.Length ? args[ci + 1] : "")); return; }
            int si = Array.IndexOf(args, "--snap");
            if (si >= 0 && si + 1 < args.Length)
            {
                Snap.On = true; HubSettings.Frozen = true;
                // v2.28: snapshots only ever see test data - a made-up library and family (a temp folder when none was given)
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST"))) { var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HallowHubSnap"); Directory.CreateDirectory(tmp); Environment.SetEnvironmentVariable("HALLOW_FAMILY_TEST", tmp); }
                { var hs0 = HubSettings.Load(); HallowLook.UseShared = hs0.UseHallowAccent; HallowLook.LookOverride = hs0.Look; HallowLook.Refresh(); }
                var sa = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(sa);
                new HubWindow(true).SnapAll(args[si + 1]); return;
            }
            int pi = Array.IndexOf(args, "--play"); string play = pi >= 0 && pi + 1 < args.Length ? args[pi + 1] : null;
            bool created;
            var mutex = new Mutex(true, @"Local\HallowHub", out created);
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HallowHubShow");
            var playEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HallowHubPlay");
            if (!created)
            {
                if (play != null) { try { Directory.CreateDirectory(HubSettings.Dir); File.WriteAllText(System.IO.Path.Combine(HubSettings.Dir, "play.txt"), play); playEvent.Set(); } catch { } }
                else showEvent.Set();
                return;
            }
            try { Checkup.ApplyHubPending(System.IO.Path.Combine(HubSettings.Dir, "bring-back.json")); } catch { }   // v2.21: Hub's own settings from Claude's sandbox (Bring settings back)
            { var hs0 = HubSettings.Load(); HallowLook.UseShared = hs0.UseHallowAccent; HallowLook.LookOverride = hs0.Look; HallowLook.Refresh(); }
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Crash.Hook(app); LoadTheme(app);
            var w = new HubWindow(args.Contains("--tray") || play != null);
            if (play != null) w.Dispatcher.BeginInvoke(new Action(() => w.PlayByKey(play)), DispatcherPriority.ApplicationIdle);
            var t = new Thread(() => { while (showEvent.WaitOne()) w.Dispatcher.BeginInvoke(new Action(w.ShowFromTray)); }) { IsBackground = true };
            t.Start();
            var t2 = new Thread(() =>
            {
                while (playEvent.WaitOne())
                {
                    string key = null; try { var f = System.IO.Path.Combine(HubSettings.Dir, "play.txt"); key = File.ReadAllText(f).Trim(); File.Delete(f); } catch { }
                    if (!string.IsNullOrEmpty(key)) w.Dispatcher.BeginInvoke(new Action(() => w.PlayByKey(key)));
                }
            }) { IsBackground = true };
            t2.Start();
            app.Run();
            GC.KeepAlive(mutex);
        }
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int which);
        // exit code 0 on ok, 1 on error, 2 when Hub isn't running; never opens a window
        static int Cmd(string line)
        {
            var h = GetStdHandle(-11); if (h == IntPtr.Zero || h == new IntPtr(-1)) AttachConsole(-1);
            string reply; int code;
            if (!Family.Running("Hub")) { reply = "error Hallow Hub isn't running"; code = 2; }
            else { reply = Family.Send("Hub", line) ?? "error no reply"; code = Family.Ok(reply) ? 0 : 1; }
            try { Console.WriteLine(reply); Console.Out.Flush(); } catch { }
            return code;
        }
        public static void LoadTheme(Application app)
        {
            string xaml;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("HallowHub.Theme.xaml")) using (var r = new StreamReader(s)) xaml = r.ReadToEnd();
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(HallowLook.Theme(xaml)));   // v2.5: with the Hallow accent
        }
        // v2.16: the theme again (mode / accent changed) and every other open window restyled in place - a dialog keeps what you typed
        public static int ReloadTheme(Application app, Window rebuilt)
        {
            var old = app.Resources.MergedDictionaries.Count > 0 ? app.Resources.MergedDictionaries[0] : null;
            LoadTheme(app);
            var now = app.Resources.MergedDictionaries[0]; int n = 0;
            foreach (Window w in app.Windows)
            {
                if (w == rebuilt) continue;
                try { n += HallowLook.Restyle(w, old, now); HallowLook.TitleBar(w); } catch { }
            }
            return n;
        }
    }

    public partial class HubWindow : Window
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(HallowLook.Hex(hex)); }   // v2.5: Hub's orange follows the Hallow accent
        static Color CI(string hex) { return (Color)ColorConverter.ConvertFromString(HallowLook.Icon(hex)); }   // v2.16: an icon / decoration colour
        static SolidColorBrush B(string hex) { return HallowLook.Brush(hex); }   // v2.16: remembers its palette colour (live restyle)
        static SolidColorBrush BI(string hex) { return HallowLook.Brush(hex, 'i'); }   // v2.16: an icon / dot / fill without text (green and amber keep their icon values)
        static SolidColorBrush BF(string hex) { return HallowLook.Brush(hex, 'f'); }      // v2.15: a filled accent area (v2.16: the light fill in light mode)
        static SolidColorBrush BD(string hex) { return HallowLook.Brush(hex, 'd'); }   // v2.15: drawn on game art - the same in both modes
        static readonly Dictionary<string, string> SourceColor = new Dictionary<string, string> {
            { "steam", "#5AB4F0" }, { "battlenet", "#4AA8FF" }, { "epic", "#ECE8F4" }, { "custom", "#A98BE0" }, { "rom", "#E0A050" }, { "ea", "#F05A5A" }, { "ubisoft", "#5A8CF0" }, { "gog", "#B48BF0" }, { "riot", "#F0505A" }, { "xbox", "#6ED28C" } };

        readonly HubSettings S = HubSettings.Load();
        List<Game> games = new List<Game>();
        readonly Tracker tracker = new Tracker();
        readonly bool startHidden;
        Game sel; bool exiting, toldTray; string sessionFor, starting; DateTime startingAt;
        List<string> presets;   // null = mixer not running

        // controls
        Button notesBtn, hideBtn; Border heroBorder; WrapPanel heroPills; StackPanel playOn; GradientStop heroTint;
        Grid heroGrid; Image heroImg, heroLogo; TextBlock heroName, heroStats; Button playBtn; ToggleButton favBtn;
        ComboBox presetBox, sortBox; CheckBox sessionBox; TextBlock sessionNote, countText, statusText, searchHint;
        Panel contRow; FrameworkElement contLabel, libLabel; ComboBox sizeBox;
        WrapPanel grid, chips; TextBox search; Border settingsPanel; ToggleButton gearBtn; ScrollViewer gridScroll;
        CheckBox startupBox; TextBlock linkInfo; ComboBox lookBox;
        readonly Dictionary<string, Border> cards = new Dictionary<string, Border>();
        WF.NotifyIcon tray; DispatcherTimer poll, slow;
        bool loading;

        public HubWindow(bool hidden)
        {
            startHidden = hidden;
            Title = "Hallow Hub"; Width = S.WinW > 900 ? S.WinW : 1180; Height = S.WinH > 600 ? S.WinH : 780; MinWidth = 960; MinHeight = 640;
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text");
            FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = AppIcon(); } catch { }
            Content = Build();
            HallowLook.Dress(this);   // v2.15: the title bar follows light / dark
            Closing += (s, e) =>
            {
                if (!exiting && S.TrayOnClose && !Snap.On)
                {
                    e.Cancel = true; Hide();
                    if (!toldTray && tray != null) { toldTray = true; tray.ShowBalloonTip(4000, "Hallow Hub", "Still running in the tray, counting your playtime. Right-click the icon to quit.", WF.ToolTipIcon.None); }
                }
                else Quit();
            };
            SizeChanged += (s, e) => { if (WindowState == WindowState.Normal && IsLoaded) { S.WinW = ActualWidth; S.WinH = ActualHeight; } };

            tracker.Started = g0 => { var m = Main(g0); if (m.Alts.Count > 0) { var dc = S.For(m.Id); dc.LastCopy = g0.Id; dc.LastCopyAt = DateTime.Now.ToString("o"); } OnStarted(m); }; tracker.Stopped = (g, since) => OnStopped(Main(g), since);
            tracker.Counted = (g0, m) => { var g = Main(g0); S.For(g.Id).AddMinutes(m, DateTime.Now); S.Save(); RefreshCard(g); if (sel == g) ShowHero(); UpdateStatus(); };
            Rescan();
            if (!Snap.On)
            {
                SetupTray();
                poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                poll.Tick += (s, e) => { try { tracker.Poll(AllCopies().ToList()); CheckReminders(); } catch { } try { FlushRestartNotes(); } catch { } if (starting != null && (DateTime.Now - startingAt).TotalSeconds > 90) { starting = null; ShowHero(); } };
                poll.Start();
                PreviewKeyDown += OnKey;
                var padTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) }; padTimer.Tick += (s, e) => { try { TickPad(); } catch { } }; padTimer.Start();
                slow = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                slow.Tick += (s, e) => { CheckLinks(); ShowFamily(false); WatchTheme(); CheckSales(false); IdleMeta(); CheckRecap(); RefreshPc(); WriteRecent(); if (sel != null && IsRunning(sel)) ShowHero(); };
                slow.Start();
                Dispatcher.BeginInvoke(new Action(() => { try { tracker.Poll(AllCopies().ToList()); } catch { } CheckLinks(); RefreshPc(); WriteRecent(); }), DispatcherPriority.ApplicationIdle);
                IsVisibleChanged += (s, e) => { if (IsVisible) RefreshPc(); };
                if (!startHidden) Show();
                StartWatching(); StartFamilyServer(); WatchTheme(); PowerAfterStart(); StartRestarter();   // v2.19: keep the family running after updates
                StartCheckupTimer();   // v2.21: the weekly Family checkup
                StartRealChecksTimer();   // v2.22: real-world checks turn green by themselves
                DateTime lastCheck;
                if (!DateTime.TryParse(S.LastUpdateCheck, out lastCheck) || (DateTime.Now - lastCheck).TotalHours > 20) CheckForUpdate(false);
                if (string.IsNullOrEmpty(S.LastSeenVersion)) { S.LastSeenVersion = Program.Version; S.Save(); }
                else if (!startHidden) Dispatcher.BeginInvoke(new Action(ShowWhatsNew), DispatcherPriority.ApplicationIdle);
                IsVisibleChanged += (s, e) => { if (IsVisible) { Dispatcher.BeginInvoke(new Action(ShowWhatsNew), DispatcherPriority.ApplicationIdle); Dispatcher.BeginInvoke(new Action(ShowCrashNotice), DispatcherPriority.ApplicationIdle); } };
                if (!startHidden) Dispatcher.BeginInvoke(new Action(ShowCrashNotice), DispatcherPriority.ApplicationIdle);
            }
            else { CheckLinks(); RefreshPc(); }
        }

        static ImageSource AppIcon()
        {
            var ico = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            return Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        // ================================================================ layout
        // v2.28: a library-launcher layout - tabs on top, the game list on the left, a calm hero, a slim bar at the bottom
        const double TopH = 56, BottomH = 34, HeroH = 300, PadX = 28;
        UIElement Build()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TopH) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BottomH) });

            // ---- top bar: logo, page tabs; search, Add game, more and Settings on the right
            var topLine = new Border { BorderBrush = B("#2A2333"), BorderThickness = new Thickness(0, 0, 0, 1) };
            var head = new DockPanel { Margin = new Thickness(20, 0, 14, 0) };
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
            try { title.Children.Add(new Image { Source = AppIcon(), Width = 24, Height = 24, Margin = new Thickness(0, 0, 9, 0) }); } catch { }
            title.Children.Add(new TextBlock { Text = "HALLOW", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = B("#F07820"), VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock { Text = " HUB", FontSize = 16, FontWeight = FontWeights.Light, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(title, Dock.Left); head.Children.Add(title);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            updateBtn = new Button { Style = (Style)FindResource("Accent"), Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
            updateBtn.Click += (s, e) => InstallUpdate(); right.Children.Add(updateBtn);
            searchBox = new Grid { Width = 240, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            search = new TextBox { Style = (Style)FindResource("Search") };
            search.TextChanged += (s, e) => { searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RebuildGrid(); };
            var si = Icons.Make("magnifying-glass", 13, (Brush)FindResource("Muted")); si.HorizontalAlignment = HorizontalAlignment.Left; si.Margin = new Thickness(13, 0, 0, 0); si.IsHitTestVisible = false;
            searchHint = new TextBlock { Text = "Search games, genres, notes...", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(35, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
            searchBox.Children.Add(search); searchBox.Children.Add(si); searchBox.Children.Add(searchHint);
            right.Children.Add(searchBox);
            var add = new Button { Content = "Add game", Style = (Style)FindResource("Btn"), Height = 34, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Add any game or app by its .exe" };
            add.Click += (s, e) => AddGame(); right.Children.Add(add);
            var more = new Button { Content = "More", Tag = "icon", Width = 36, Height = 36, Padding = new Thickness(0), Style = (Style)FindResource("Ghost"), ToolTip = "More: Big Picture, Surprise me, Storage, emulators, Steam account, refresh" };
            more.Click += (s, e) => { var m = MoreMenu(); m.PlacementTarget = more; m.Placement = PlacementMode.Bottom; m.IsOpen = true; };
            right.Children.Add(more);
            gearBtn = new ToggleButton { Content = "Settings", Tag = "icon", Width = 36, Height = 36, Padding = new Thickness(0), Style = (Style)FindResource("Ghost"), ToolTip = "Settings" };
            gearBtn.Checked += (s, e) => { newsPanel.Visibility = Visibility.Collapsed; aboutPanel.Visibility = Visibility.Collapsed; wishPanel.Visibility = Visibility.Collapsed; if (statsBtn != null) statsBtn.IsChecked = false; if (storageBtn != null) storageBtn.IsChecked = false; if (familyBtn != null) familyBtn.IsChecked = false; settingsPanel.Visibility = Visibility.Visible; CheckLinks(); };
            gearBtn.Unchecked += (s, e) => settingsPanel.Visibility = Visibility.Collapsed;
            right.Children.Add(gearBtn);
            DockPanel.SetDock(right, Dock.Right); head.Children.Add(right);
            // the pages, as tabs
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Stretch };
            libTab = new ToggleButton { Content = "LIBRARY", Style = (Style)FindResource("Tab"), IsChecked = true, ToolTip = "Your games" };
            libTab.Click += (s, e) => ShowLibrary();
            tabs.Children.Add(libTab);
            familyBtn = BuildFamilyButton();   // v2.17: the Hallow family page
            familyBtn.Content = "HALLOW APPS"; familyBtn.Style = (Style)FindResource("Tab"); familyBtn.Tag = null;
            foreach (var p in new[] { WidthProperty, HeightProperty, PaddingProperty, MarginProperty }) familyBtn.ClearValue(p);
            tabs.Children.Add(familyBtn);
            statsBtn = new ToggleButton { Content = "STATS", Style = (Style)FindResource("Tab"), ToolTip = "Your playtime" };
            statsBtn.Checked += (s, e) => { newsPanel.Visibility = Visibility.Collapsed; aboutPanel.Visibility = Visibility.Collapsed; wishPanel.Visibility = Visibility.Collapsed; gearBtn.IsChecked = false; if (storageBtn != null) storageBtn.IsChecked = false; if (familyBtn != null) familyBtn.IsChecked = false; FillStats(); statsPanel.Visibility = Visibility.Visible; };
            statsBtn.Unchecked += (s, e) => statsPanel.Visibility = Visibility.Collapsed;
            tabs.Children.Add(statsBtn);
            storageBtn = new ToggleButton { Content = "STORAGE", Style = (Style)FindResource("Tab"), ToolTip = "Storage: what's on each drive, and what you could free up" };
            storageBtn.Checked += (s, e) => { newsPanel.Visibility = Visibility.Collapsed; aboutPanel.Visibility = Visibility.Collapsed; wishPanel.Visibility = Visibility.Collapsed; gearBtn.IsChecked = false; statsBtn.IsChecked = false; if (familyBtn != null) familyBtn.IsChecked = false; FillStorage(); storagePanel.Visibility = Visibility.Visible; };
            storageBtn.Unchecked += (s, e) => storagePanel.Visibility = Visibility.Collapsed;
            tabs.Children.Add(storageBtn);
            wishBtn = new Button { Content = "SALES", Style = (Style)FindResource("Tab"), ToolTip = "Your Steam wishlist and what's on sale" };
            wishBtn.Click += (s, e) => OpenWishlist(); UpdateWishButton();   // v2.7
            tabs.Children.Add(wishBtn);
            BuildSteamAccountButton();   // v2.18: switch Steam accounts - now in the More menu
            head.Children.Add(tabs);
            topLine.Child = head;
            Grid.SetRow(topLine, 0); root.Children.Add(topLine);

            // ---- body: the game list on the left, the library on the right
            var body = new Grid();
            sideCol = new ColumnDefinition { Width = new GridLength(244) };
            body.ColumnDefinitions.Add(sideCol); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sideBar = BuildSidebar(); Grid.SetColumn(sideBar, 0); body.Children.Add(sideBar);
            var lib = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };

            // ---- hero: the selected game's art, fading into the page
            var hero = heroBorder = new Border { Height = HeroH, ClipToBounds = true };
            heroGrid = new Grid();
            heroImg = new Image { Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Center };
            heroGrid.Children.Add(heroImg); StartBannerMotion();
            var bg = ((SolidColorBrush)FindResource("Bg")).Color;
            var fadeL = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            heroTint = new GradientStop(Color.FromArgb((byte)(HallowLook.Light ? 215 : 190), bg.R, bg.G, bg.B), 0); fadeL.GradientStops.Add(heroTint);
            fadeL.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(HallowLook.Light ? 120 : 70), bg.R, bg.G, bg.B), 0.45)); fadeL.GradientStops.Add(new GradientStop(Color.FromArgb(0, bg.R, bg.G, bg.B), 0.8));
            heroGrid.Children.Add(new Rectangle { Fill = fadeL });
            var fadeB = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fadeB.GradientStops.Add(new GradientStop(Color.FromArgb(0, bg.R, bg.G, bg.B), 0.35)); fadeB.GradientStops.Add(new GradientStop(Color.FromArgb(200, bg.R, bg.G, bg.B), 0.78)); fadeB.GradientStops.Add(new GradientStop(bg, 1));
            heroGrid.Children.Add(new Rectangle { Fill = fadeB });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(PadX + 4, 0, PadX, 14), MaxWidth = 560 };
            heroLogo = new Image { MaxHeight = 110, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 4) };
            heroName = new TextBlock { FontSize = 34, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Foreground = HallowLook.Light ? (Brush)FindResource("Text") : Brushes.White };
            heroStats = new TextBlock { FontSize = 13, Foreground = B("#D6D0E2"), Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            heroPills = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            info.Children.Add(heroLogo); info.Children.Add(heroName); info.Children.Add(heroStats); info.Children.Add(heroPills);
            heroGrid.Children.Add(info);
            hero.Child = heroGrid;
            lib.Children.Add(hero);

            // ---- the play bar: one big Play, the playtime, a few quiet buttons and a "more" menu
            var bar1 = new DockPanel { Margin = new Thickness(PadX, 2, PadX, 0), LastChildFill = true };
            playBtn = new Button { Content = "Play", Style = (Style)FindResource("Accent"), FontSize = 15, Padding = new Thickness(34, 11, 34, 11), MinWidth = 168, VerticalAlignment = VerticalAlignment.Center };
            playBtn.Click += (s, e) => Play(sel);
            DockPanel.SetDock(playBtn, Dock.Left); bar1.Children.Add(playBtn);
            var acts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Func<string, string, Button> ghost = (label, tip) => new Button { Content = label, Tag = "icon", Width = 38, Height = 38, Padding = new Thickness(0), Style = (Style)FindResource("Ghost"), Margin = new Thickness(2, 0, 0, 0), ToolTip = tip };
            favBtn = new ToggleButton { Content = "Favorite", Tag = "icon", Width = 38, Height = 38, Padding = new Thickness(0), Style = (Style)FindResource("Ghost"), ToolTip = "Favorite" };
            favBtn.Click += (s, e) => { if (sel == null) return; S.For(sel.Id).Favorite = favBtn.IsChecked == true; S.Save(); RefreshCard(sel); ShowHero(); if (S.Filter == "fav") RebuildGrid(); };
            acts.Children.Add(favBtn);
            var gset = ghost("Game settings", "Game settings: launch options, run as admin, collections, apps to close");
            gset.Click += (s, e) => GameSettings(sel); acts.Children.Add(gset);
            notesBtn = ghost("Notes", "Notes");
            notesBtn.Click += (s, e) => ShowNotes(notesBtn); acts.Children.Add(notesBtn);
            linksBtn = ghost("Links", "Links: store page, guides, PCGamingWiki, HowLongToBeat and your own");
            linksBtn.Click += (s, e) => ShowLinks(linksBtn); acts.Children.Add(linksBtn);   // v2.12
            var gmore = ghost("More", "More: open folder, news, about, pin, hide, shortcut, uninstall...");
            gmore.Click += (s, e) => { if (sel == null) return; var m = GameMenu(sel); m.PlacementTarget = gmore; m.Placement = PlacementMode.Bottom; m.IsOpen = true; };
            acts.Children.Add(gmore);
            // still made (their state follows the game), reached from the "more" menu now
            hideBtn = new Button { Content = "Hide" }; hideBtn.Click += (s, e) => ToggleHide(sel);
            aboutBtn = new Button { Content = "About", Visibility = Visibility.Collapsed }; aboutBtn.Click += (s, e) => OpenAbout();   // v2.8
            newsBtn = new Button { Content = "News", Visibility = Visibility.Collapsed }; newsBtn.Click += (s, e) => OpenNews();   // v2.6
            DockPanel.SetDock(acts, Dock.Right); bar1.Children.Add(acts);
            heroFacts = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(26, 0, 12, 0) };
            bar1.Children.Add(heroFacts);
            lib.Children.Add(bar1);
            // status, rating and which copy plays
            var bar2 = new WrapPanel { Margin = new Thickness(PadX, 12, PadX, 0) };
            var sr = BuildStatusRow(); ((FrameworkElement)sr).Margin = new Thickness(0, 0, 24, 6); bar2.Children.Add(sr);   // v2.10: play status and your rating
            playOn = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6), VerticalAlignment = VerticalAlignment.Center };
            bar2.Children.Add(playOn);
            lib.Children.Add(bar2);

            // ---- "When this game starts" + "Your PC right now": one quiet section that opens below
            var startHead = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(PadX, 10, PadX, 0), Padding = new Thickness(0, 6, 0, 6), ToolTip = "Hallow Mixer preset, Hallow Toolkit game session, apps to close and your PC" };
            var shp = new DockPanel();
            startChevron = Icons.Make("plus", 10, (Brush)FindResource("Muted")); startChevron.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(startChevron, Dock.Left); shp.Children.Add(startChevron);
            var shl = new TextBlock { Text = "WHEN THIS GAME STARTS", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(shl, Dock.Left); shp.Children.Add(shl);
            startSummary = new TextBlock { FontSize = 11.5, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            shp.Children.Add(startSummary);
            startHead.Child = shp;
            startHead.MouseLeftButtonUp += (s, e) => { S.StartOpen = !S.StartOpen; S.Save(); ShowStartSection(); };
            lib.Children.Add(startHead);
            startCard = new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(10), Padding = new Thickness(20, 16, 20, 12), Margin = new Thickness(PadX, 6, PadX, 0) };
            var cols = new WrapPanel();
            var c1 = new StackPanel { Width = 250, Margin = new Thickness(0, 0, 32, 8) };
            c1.Children.Add(new TextBlock { Text = "Hallow Mixer preset", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, Margin = new Thickness(0, 0, 0, 4) });
            presetBox = new ComboBox { Style = (Style)FindResource("Combo"), Margin = new Thickness(0, 0, 0, 12) };
            presetBox.SelectionChanged += (s, e) =>
            {
                if (loading || sel == null) return; var it = presetBox.SelectedItem as ComboBoxItem; if (it == null) return;
                S.For(sel.Id).Preset = (it.Tag as string) ?? ""; S.Save(); ShowStartSection();
                Status(S.For(sel.Id).Preset == "" ? sel.Name + " leaves your mixer as it is." : sel.Name + " switches Hallow Mixer to \"" + S.For(sel.Id).Preset + "\" when it starts.");
            };
            c1.Children.Add(presetBox);
            sessionBox = new CheckBox { Content = new TextBlock { Text = "Hallow Toolkit game session", TextWrapping = TextWrapping.Wrap }, Style = (Style)FindResource("Switch"), Margin = new Thickness(0) };
            sessionBox.Click += (s, e) =>
            {
                if (sel == null) return; S.For(sel.Id).GameSession = sessionBox.IsChecked == true; S.Save(); ShowStartSection();
                Status(sessionBox.IsChecked == true ? "Game session on for " + sel.Name + ": Boost apps close and High performance power while it runs, back after." : "No game session for " + sel.Name + ".");
            };
            c1.Children.Add(sessionBox);
            sessionNote = new TextBlock { Foreground = (Brush)FindResource("Muted"), FontSize = 11, Margin = new Thickness(52, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
            c1.Children.Add(sessionNote);
            cols.Children.Add(c1);
            var c2 = new StackPanel { Width = 230, Margin = new Thickness(0, 0, 32, 8) };
            c2.Children.Add(new TextBlock { Text = "Also", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5 });
            c2.Children.Add(BuildChecklist());   // v2.12
            var none = new TextBlock { Text = "Nothing else - add apps to close or open in Game settings.", Foreground = (Brush)FindResource("Muted"), FontSize = 11, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Cursor = Cursors.Hand };
            none.MouseLeftButtonUp += (s, e) => GameSettings(sel);
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(VisibilityProperty, typeof(TextBlock)).AddValueChanged(checklist, (s, e) => none.Visibility = checklist.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible);
            c2.Children.Add(none);
            cols.Children.Add(c2);
            var c3 = new StackPanel { Width = 250, Margin = new Thickness(0, 0, 0, 8) };
            var pch = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var pci = Icons.Make("heart-pulse", 11, (Brush)FindResource("Muted")); pci.Margin = new Thickness(0, 0, 8, 0); pch.Children.Add(pci);
            pch.Children.Add(new TextBlock { Text = "Your PC right now", FontSize = 11.5, Foreground = (Brush)FindResource("Muted") });
            c3.Children.Add(pch);
            pcPills = new WrapPanel(); c3.Children.Add(pcPills);
            cols.Children.Add(c3);
            startCard.Child = cols;
            lib.Children.Add(startCard);
            // v2.2: Hallow family cards (Saves, Clips, Overlay) - only the apps you have
            familyRow = new Grid { Margin = new Thickness(PadX, 16, PadX, 0), Visibility = Visibility.Collapsed };
            lib.Children.Add(familyRow);

            // ---- continue playing (the shelf), then the library with a slim toolbar: its title on the left; surprise, filters, size,
            // view and sort on the right; the quick filters under it
            contLabel = SectionLabel("CONTINUE PLAYING", "clock"); ((FrameworkElement)contLabel).Margin = new Thickness(PadX, 26, PadX, 10);
            contRow = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(PadX - 8, 0, PadX - 8, 4) };
            lib.Children.Add(contLabel); lib.Children.Add(contRow);
            var bar = new DockPanel { Margin = new Thickness(PadX - 6, 22, PadX, 4) };
            sortBox = new ComboBox { Style = (Style)FindResource("Combo"), Width = 150, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Sort" };
            foreach (var o in new[] { Tuple.Create("recent", "Recently played"), Tuple.Create("played", "Most played"), Tuple.Create("name", "A to Z"), Tuple.Create("size", "Biggest first"), Tuple.Create("store", "By store"), Tuple.Create("rating", "Your rating"), Tuple.Create("status", "Play status") })
                sortBox.Items.Add(new ComboBoxItem { Content = o.Item2, Tag = o.Item1 });
            sortBox.SelectionChanged += (s, e) => { if (loading) return; var it = sortBox.SelectedItem as ComboBoxItem; if (it != null) { S.Sort = (string)it.Tag; S.Save(); RebuildGrid(); } };
            DockPanel.SetDock(sortBox, Dock.Right); bar.Children.Add(sortBox);
            var viewBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            sizeBox = new ComboBox { Style = (Style)FindResource("Combo"), Width = 92, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Cover size" };
            foreach (var o in new[] { Tuple.Create(0.8, "Small"), Tuple.Create(1.0, "Medium"), Tuple.Create(1.25, "Large") }) sizeBox.Items.Add(new ComboBoxItem { Content = o.Item2, Tag = o.Item1 });
            sizeBox.SelectedItem = sizeBox.Items.Cast<ComboBoxItem>().OrderBy(i => Math.Abs((double)i.Tag - S.CardScale)).First();
            sizeBox.SelectionChanged += (s, e) => { var it = sizeBox.SelectedItem as ComboBoxItem; if (it == null || loading) return; S.CardScale = (double)it.Tag; S.Save(); RebuildGrid(); };
            var gridV = new ToggleButton { Content = "Grid view", Tag = "icon", Width = 32, Height = 30, Padding = new Thickness(0), Style = (Style)FindResource("Ghost"), ToolTip = "Covers", IsChecked = S.View != "list" };
            var listV = new ToggleButton { Content = "List view", Tag = "icon", Width = 32, Height = 30, Padding = new Thickness(0), Style = (Style)FindResource("Ghost"), ToolTip = "List", IsChecked = S.View == "list" };
            gridV.Click += (s, e) => { S.View = "grid"; S.Save(); gridV.IsChecked = true; listV.IsChecked = false; sizeBox.Visibility = Visibility.Visible; RebuildGrid(); };
            listV.Click += (s, e) => { S.View = "list"; S.Save(); gridV.IsChecked = false; listV.IsChecked = true; sizeBox.Visibility = Visibility.Collapsed; RebuildGrid(); };
            sizeBox.Visibility = S.View == "list" ? Visibility.Collapsed : Visibility.Visible;
            viewBar.Children.Add(sizeBox); viewBar.Children.Add(gridV); viewBar.Children.Add(listV);
            DockPanel.SetDock(viewBar, Dock.Right); bar.Children.Add(viewBar);
            var fbtn = BuildFilterButton(); filterBtn.Style = (Style)FindResource("Pill"); filterBtn.Margin = new Thickness(0, 0, 4, 0); DockPanel.SetDock(fbtn, Dock.Right); bar.Children.Add(fbtn);
            var dice = new Button { Content = "Surprise me", Tag = "icon", Width = 32, Height = 30, Padding = new Thickness(0), Style = (Style)FindResource("Ghost"), Margin = new Thickness(0, 0, 4, 0), ToolTip = "What should I play? (Ctrl+R, or X on a controller)\nRight-click: from your backlog, a favorite, or one you haven't played in a while" };
            dice.Click += (s, e) => Surprise(); dice.ContextMenu = PickMenu();   // v2.12: right-click - from your backlog, a favorite, an old one
            DockPanel.SetDock(dice, Dock.Right); bar.Children.Add(dice);
            sideToggle = new Button { Content = "Game list", Tag = "icon", Width = 32, Height = 30, Padding = new Thickness(0), Style = (Style)FindResource("Ghost"), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Show or hide the game list" };
            sideToggle.Click += (s, e) => ToggleSidebar();
            DockPanel.SetDock(sideToggle, Dock.Left); bar.Children.Add(sideToggle);
            var lt = new TextBlock { Text = "LIBRARY", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(lt, Dock.Left); bar.Children.Add(lt);
            countText = new TextBlock { Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 12, 0), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
            bar.Children.Add(countText);
            lib.Children.Add(bar);
            chips = new WrapPanel { Margin = new Thickness(PadX - 6, 4, PadX, 6) };   // v2.3: wraps to a second line when there are many filters
            lib.Children.Add(chips);
            grid = new WrapPanel { Margin = new Thickness(PadX - 8, 6, PadX - 8, 0) };
            libLabel = new Border();   // v2.28: the toolbar says LIBRARY now
            lib.Children.Add(grid);
            gridScroll = new ScrollViewer { Content = lib, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetColumn(gridScroll, 1); body.Children.Add(gridScroll);
            Grid.SetRow(body, 1); root.Children.Add(body);
            root.SizeChanged += (s, e) => FitWidth(e.NewSize.Width);

            // ---- the bottom bar: what's going on (playing, updates) on the left, today's playtime on the right
            var foot = new Border { BorderBrush = B("#2A2333"), BorderThickness = new Thickness(0, 1, 0, 0) };
            var fd = new DockPanel { Margin = new Thickness(20, 0, 20, 0) };
            todayText = new TextBlock { Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(todayText, Dock.Right); fd.Children.Add(todayText);
            dlBox = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 12, 0) };
            var dli = Icons.Make("download", 11, BI("#5AB4F0")); dli.Margin = new Thickness(0, 0, 8, 0); dlBox.Children.Add(dli);
            var track = new Border { Width = 90, Height = 4, CornerRadius = new CornerRadius(2), Background = B("#3A3248"), VerticalAlignment = VerticalAlignment.Center };
            dlFill = new Border { Width = 0, Height = 4, CornerRadius = new CornerRadius(2), Background = BI("#5AB4F0"), HorizontalAlignment = HorizontalAlignment.Left };
            track.Child = dlFill; dlBox.Children.Add(track);
            DockPanel.SetDock(dlBox, Dock.Left); fd.Children.Add(dlBox);
            statusText = new TextBlock { Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 16, 0) };
            fd.Children.Add(statusText);
            foot.Child = fd;
            Grid.SetRow(foot, 2); root.Children.Add(foot);

            var outer = new Grid(); outer.Children.Add(root);
            settingsPanel = BuildSettings(); outer.Children.Add(settingsPanel);
            statsPanel = BuildStats(); outer.Children.Add(AsPage(statsPanel, false));
            storagePanel = BuildStorage(); outer.Children.Add(AsPage(storagePanel, false));
            familyPanel = BuildFamilyPage(); outer.Children.Add(AsPage(familyPanel, false));   // v2.17
            newsPanel = BuildNews(); outer.Children.Add(AsPage(newsPanel, false));
            wishPanel = BuildWishlist(); outer.Children.Add(AsPage(wishPanel, false));
            aboutPanel = BuildAbout(); outer.Children.Add(AsPage(aboutPanel, false));
            ShowStartSection();
            return outer;
        }

        // ---- v1.1 stats: totals, a 14-day chart and your top games
        Border statsPanel; ToggleButton statsBtn; StackPanel statsBody; ScrollViewer statsScroll;
        Border BuildStats()
        {
            var b = new Border { Background = B("#211B2A"), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24, 16, 24, 22), Width = 800, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 62, 0, 0), Visibility = Visibility.Collapsed };
            b.Effect = HallowLook.Shadow(30, 0.6, 4);
            var sp = new StackPanel();
            var sh = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var close = new Button { Content = "x", Tag = "icon", Style = (Style)FindResource("Btn"), Width = 30, Height = 28, Padding = new Thickness(0), ToolTip = "Close" };
            close.Click += (s, e) => statsBtn.IsChecked = false;
            DockPanel.SetDock(close, Dock.Right); sh.Children.Add(close);
            var ci = Icons.Make("chart-line", 16, B("#F07820")); ci.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(ci, Dock.Left); sh.Children.Add(ci);
            var exp = new Button { Content = "Export", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Save your playtime as a spreadsheet (CSV)" };
            exp.Click += (s, e) => ExportCsv();
            DockPanel.SetDock(exp, Dock.Right); sh.Children.Add(exp);
            sh.Children.Add(new TextBlock { Text = "Your playtime", FontWeight = FontWeights.Bold, FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(sh);
            statsBody = new StackPanel();
            statsScroll = new ScrollViewer { Content = statsBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, -12, 0), Padding = new Thickness(0, 0, 12, 0) };
            SizeChanged += (s, e) => statsScroll.MaxHeight = Math.Max(240, ActualHeight - 150);   // v2.12: the page grew - it scrolls on small windows
            statsScroll.MaxHeight = Math.Max(240, Height - 150);
            sp.Children.Add(statsScroll);
            b.Child = sp; return b;
        }
        void FillStats()
        {
            statsBody.Children.Clear();
            var vis = games.Where(g => !S.For(g.Id).Hidden).ToList();
            double today = S.Games.Values.Sum(x => x.MinutesSince(DateTime.Today)), week = S.Games.Values.Sum(x => x.MinutesSince(DateTime.Today.AddDays(-6)));
            double two = vis.Sum(g => Recent(g)), all = vis.Sum(g => Total(g));
            var tiles = new UniformGrid { Rows = 1, Margin = new Thickness(-5, 0, -5, 18) };
            Action<string, double, string, string> tile = (label, mins, col, sub) =>
            {
                var t = new StackPanel();
                t.Children.Add(new TextBlock { Text = label, Foreground = (Brush)FindResource("Muted"), FontSize = 11.5 });
                t.Children.Add(new TextBlock { Text = mins < 1 ? "0 min" : HubSettings.Duration(mins), FontSize = 22, FontWeight = FontWeights.Bold, Foreground = B(col), Margin = new Thickness(0, 2, 0, 0) });
                if (sub != null) t.Children.Add(new TextBlock { Text = sub, Foreground = (Brush)FindResource("Muted"), FontSize = 10.5 });
                tiles.Children.Add(new Border { Child = t, Background = B("#2A2333"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(5, 0, 5, 0) });
            };
            var streak = Streak();
            tile("Today", today, "#F07820", streak >= 2 ? "streak: " + streak + " days in a row" : null); tile("This week", week, "#A98BE0", null);
            tile("Last 2 weeks", two, "#6ED28C", "Steam + Hallow Hub"); tile("All time", all, "#5AB4F0", vis.Count(g => Total(g) >= 1) + " games played");
            statsBody.Children.Add(tiles);
            // v2.6: 14 days, hours per week or per month - all from Hub's own day-by-day history
            var mode = statsMode;
            var head = new DockPanel();
            var seg = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            foreach (var m in new[] { Tuple.Create("days", "14 days"), Tuple.Create("weeks", "Weeks"), Tuple.Create("months", "Months") })
            {
                var key = m.Item1;
                var chip = new ToggleButton { Content = m.Item2, Style = (Style)FindResource("Chip"), Padding = new Thickness(10, 2, 10, 3), FontSize = 11, Margin = new Thickness(6, 0, 0, 0), IsChecked = mode == key };
                chip.Click += (s, e) => { statsMode = key; FillStats(); };
                seg.Children.Add(chip);
            }
            DockPanel.SetDock(seg, Dock.Right); head.Children.Add(seg);
            head.Children.Add(SectionLabel(mode == "weeks" ? "HOURS PER WEEK - LAST 12 WEEKS" : mode == "months" ? "HOURS PER MONTH - LAST 12 MONTHS" : "LAST 14 DAYS", mode == "days" ? "calendar-days" : "chart-column"));
            statsBody.Children.Add(head);
            var labels = new List<string>(); var per = new List<double>(); var tips = new List<string>(); DateTime periodStart;
            if (mode == "weeks")
            {
                var monday = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));
                periodStart = monday.AddDays(-7 * 11);
                for (int i = 0; i < 12; i++)
                {
                    var from = periodStart.AddDays(7 * i); var v = HubMinutes(from, from.AddDays(7));
                    labels.Add(from.ToString("d MMM")); per.Add(v); tips.Add("Week of " + from.ToString("d MMM yyyy") + ": " + (v < 1 ? "nothing" : HubSettings.Duration(v)));
                }
            }
            else if (mode == "months")
            {
                var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                periodStart = first.AddMonths(-11);
                for (int i = 0; i < 12; i++)
                {
                    var from = periodStart.AddMonths(i); var v = HubMinutes(from, from.AddMonths(1));
                    labels.Add(from.ToString("MMM")); per.Add(v); tips.Add(from.ToString("MMMM yyyy") + ": " + (v < 1 ? "nothing" : HubSettings.Duration(v)));
                }
            }
            else
            {
                periodStart = DateTime.Today.AddDays(-13);
                for (int i = 0; i < 14; i++)
                {
                    var d = periodStart.AddDays(i); var v = HubMinutes(d, d.AddDays(1));
                    labels.Add(d.ToString("ddd").Substring(0, 2) + " " + d.Day); per.Add(v); tips.Add(d.ToString("dddd d MMM") + ": " + (v < 1 ? "nothing" : HubSettings.Duration(v)));
                }
            }
            statsBody.Children.Add(BarChart(labels, per, tips, mode == "days" ? "This fills in day by day as you play with Hallow Hub running." : "This fills in as you play with Hallow Hub running - it counts from the day you installed it."));            // top games
            var two2 = new Grid(); two2.ColumnDefinitions.Add(new ColumnDefinition()); two2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); two2.ColumnDefinitions.Add(new ColumnDefinition());
            Func<string, string, Func<Game, double>, FrameworkElement> top = (title, icon, f) =>
            {
                var sp = new StackPanel(); sp.Children.Add(SectionLabel(title, icon));
                var list = vis.Where(g => f(g) >= 1).OrderByDescending(f).Take(6).ToList();
                double mx = list.Count > 0 ? f(list[0]) : 1;
                foreach (var g in list)
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 9), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = "Show " + g.Name };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                    row.Children.Add(new TextBlock { Text = g.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                    var ac = Accent(g);
                    var bar = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(ac), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
                    var track = new Grid(); track.Children.Add(new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = B("#2E2639"), Margin = new Thickness(8, 0, 8, 0) }); track.Children.Add(bar);
                    double frac = f(g) / mx; track.SizeChanged += (o, e) => bar.Width = Math.Max(8, (track.ActualWidth - 16) * frac);
                    Grid.SetColumn(track, 1); row.Children.Add(track);
                    var t = new TextBlock { Text = HubSettings.Duration(f(g)), Foreground = (Brush)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(t, 2); row.Children.Add(t);
                    var gg = g; row.MouseLeftButtonUp += (o, e) => { statsBtn.IsChecked = false; Select(gg); };
                    sp.Children.Add(row);
                }
                if (list.Count == 0) sp.Children.Add(new TextBlock { Text = "Nothing yet.", Foreground = (Brush)FindResource("Muted") });
                return sp;
            };
            var a1 = top("MOST PLAYED", "trophy", Total);
            var ps = periodStart;
            var a2 = mode == "days" ? top("LAST 2 WEEKS", "fire", Recent) : top(mode == "weeks" ? "TOP GAMES - LAST 12 WEEKS" : "TOP GAMES - LAST 12 MONTHS", "fire", g => S.For(g.Id).MinutesSince(ps));
            Grid.SetColumn(a2, 2); two2.Children.Add(a1); two2.Children.Add(a2);
            statsBody.Children.Add(two2);
            statsBody.Children.Add(LibrarySection());   // v2.12: play status split, backlog, finished, ratings
            statsBody.Children.Add(RecentSessions());
        }

        // ---- v2.6 charts
        string statsMode = "days";
        // minutes Hub counted for every game between two days (from inclusive, to exclusive)
        double HubMinutes(DateTime from, DateTime to)
        {
            string a = from.ToString("yyyy-MM-dd"), b = to.ToString("yyyy-MM-dd"); double t = 0;
            foreach (var gd in S.Games.Values)
                foreach (var d in gd.Days)
                {
                    var p = d.Split('='); double v;
                    if (p.Length == 2 && string.CompareOrdinal(p[0], a) >= 0 && string.CompareOrdinal(p[0], b) < 0 && double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) t += v;
                }
            return t;
        }
        // bars in the Hub look; the last bar (now) is highlighted
        FrameworkElement BarChart(List<string> labels, List<double> per, List<string> tips, string empty)
        {
            int n = per.Count; double max = Math.Max(per.Count > 0 ? per.Max() : 0, 1);
            var chart = new Grid { Height = 150, Margin = new Thickness(0, 0, 0, 18) };
            for (int i = 0; i < n; i++) chart.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < n; i++)
            {
                var col = new DockPanel { Margin = new Thickness(4, 0, 4, 0) };
                var lbl = new TextBlock { Text = labels[i], FontSize = 10, Foreground = i == n - 1 ? B("#F07820") : (Brush)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
                DockPanel.SetDock(lbl, Dock.Bottom); col.Children.Add(lbl);
                var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                fill.GradientStops.Add(new GradientStop(C("#FF9A45"), 0)); fill.GradientStops.Add(new GradientStop(C("#F05A8C"), 1));
                var h = per[i] < 1 ? 3 : Math.Max(6, per[i] / max * 112);
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
                if (per[i] >= 60) stack.Children.Add(new TextBlock { Text = (per[i] / 60).ToString(per[i] >= 600 ? "0" : "0.#") + "h", FontSize = 9.5, Foreground = (Brush)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3) });
                stack.Children.Add(new Border { Height = h, CornerRadius = new CornerRadius(5), Background = per[i] < 1 ? B("#2E2639") : (Brush)fill, ToolTip = tips[i] });
                col.Children.Add(stack);
                Grid.SetColumn(col, i); chart.Children.Add(col);
            }
            if (per.Count == 0 || per.Max() < 1)
                { var note = new TextBlock { Text = empty, Foreground = (Brush)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 30) }; Grid.SetColumnSpan(note, Math.Max(1, n)); chart.Children.Add(note); }
            return chart;
        }

        Border BuildSettings()
        {
            var b = new Border { Background = B("#241E2E"), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18, 14, 18, 16), Width = 620, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 62, 22, 0), Visibility = Visibility.Collapsed };
            b.Effect = HallowLook.Shadow(24, 0.5, 4);
            var sp = new StackPanel();
            var sh = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var close = new Button { Content = "x", Tag = "icon", Style = (Style)FindResource("Btn"), Width = 30, Height = 28, Padding = new Thickness(0), ToolTip = "Close settings" };
            close.Click += (s, e) => gearBtn.IsChecked = false;
            DockPanel.SetDock(close, Dock.Right); sh.Children.Add(close);
            var gi = Icons.Make("gear", 16, B("#F07820")); gi.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(gi, Dock.Left); sh.Children.Add(gi);
            sh.Children.Add(new TextBlock { Text = "Settings", FontWeight = FontWeights.Bold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(sh);
            Func<string, bool, Action<bool>, CheckBox> sw = (text, on, set) =>
            {
                var c = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = on, Style = (Style)FindResource("Switch") };
                c.Click += (s, e) => { set(c.IsChecked == true); S.Save(); };
                sp.Children.Add(c); return c;
            };
            sw("Keep running in the tray when I close the window (so playtime keeps counting)", S.TrayOnClose, v => S.TrayOnClose = v);
            startupBox = sw("Start with Windows (in the tray)", StartsWithWindows(), v => SetStartWithWindows(v));
            sw("Show hidden games", S.ShowHidden, v => { S.ShowHidden = v; RebuildGrid(); });
            sw("One Hallow icon: the other Hallow apps use this tray icon (their menus are in Hub's menu)", S.OneIcon, v => { S.OneIcon = v; Status(v ? "One Hallow icon is on - the other Hallow apps hide their own tray icons in a few seconds." : "One Hallow icon is off - every Hallow app shows its own tray icon again in a few seconds."); });
            AddRestartSettings(sp);   // v2.19
            sw("Family checkup once a week (quietly - one note only when something needs a look, never during a game)", S.CheckupWeekly, v => S.CheckupWeekly = v);   // v2.21
            sw("Use the Hallow accent (the colour picked in Hallow Toolkit)", S.UseHallowAccent, v => { S.UseHallowAccent = v; HallowLook.UseShared = v; S.Save(); Dispatcher.BeginInvoke(new Action(() => ApplyAccent(false))); });
            // v2.15: Look - Follow Hallow (the family mode from Hallow Toolkit) / Light / Dark
            var lk = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            lookBox = new ComboBox { Style = (Style)FindResource("Combo"), Width = 150 };
            foreach (var o in new[] { new[] { "hallow", "Follow Hallow" }, new[] { "light", "Light" }, new[] { "dark", "Dark" } }) lookBox.Items.Add(new ComboBoxItem { Content = o[1], Tag = o[0] });
            lookBox.SelectedItem = lookBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == HallowLook.NormalLook(S.Look)) ?? lookBox.Items[0];
            lookBox.SelectionChanged += (s, e) =>
            {
                var it = lookBox.SelectedItem as ComboBoxItem; if (it == null || (string)it.Tag == HallowLook.NormalLook(S.Look)) return;
                S.Look = (string)it.Tag; HallowLook.LookOverride = S.Look; S.Save(); Dispatcher.BeginInvoke(new Action(() => ApplyAccent(false)));
            };
            DockPanel.SetDock(lookBox, Dock.Right); lk.Children.Add(lookBox);
            var lt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lt.Children.Add(new TextBlock { Text = "Look" });
            lt.Children.Add(new TextBlock { Text = "Follow Hallow: " + (HallowLook.FamilyMode == "system" ? "same as Windows" : HallowLook.FamilyMode) + " (set in Hallow Toolkit)", Foreground = (Brush)FindResource("Muted"), FontSize = 11, TextWrapping = TextWrapping.Wrap });
            lk.Children.Add(lt); sp.Children.Add(lk);
            sw("Moving banner (the art drifts slowly)", !S.StillBanner, v => { S.StillBanner = !v; if (v) StartBannerMotion(); else StopBannerMotion(); });
            sw("Weekly recap on Mondays (a pop-up with last week's playtime)", S.WeeklyRecap, v => S.WeeklyRecap = v);
            var dc = Discord.Connected;
            var d1 = sw("Post to Discord when I start a game" + (dc ? "" : "  (connect Discord in Hallow Toolkit first)"), S.DiscordOnStart && dc, v => S.DiscordOnStart = v); d1.IsEnabled = dc;
            var d2 = sw("Post my weekly recap to Discord too", S.DiscordRecap && dc, v => S.DiscordRecap = v); d2.IsEnabled = dc;
            var br = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var brBox = new ComboBox { Style = (Style)FindResource("Combo"), Width = 150 };
            foreach (var m in new[] { 0, 60, 90, 120, 180 }) brBox.Items.Add(new ComboBoxItem { Content = m == 0 ? "Off" : "After " + HubSettings.Duration(m).Replace("h 00m", "h"), Tag = m });
            brBox.SelectedItem = brBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == S.BreakMinutes) ?? brBox.Items[0];
            brBox.SelectionChanged += (s, e) => { var it = brBox.SelectedItem as ComboBoxItem; if (it != null) { S.BreakMinutes = (int)it.Tag; S.Save(); } };
            DockPanel.SetDock(brBox, Dock.Right); br.Children.Add(brBox);
            br.Children.Add(new TextBlock { Text = "Break reminder", VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(br);
            AddSaleSettings(sp);   // v2.7
            var lib = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var bk = new Button { Content = "Back up library...", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Collections, notes, pins, hidden games, names and pictures, settings and playtime history in one zip - for another PC or a fresh Windows" };
            bk.Click += (s, e) => ExportLibrary();
            var im = new Button { Content = "Import library...", Style = (Style)FindResource("Btn"), ToolTip = "Bring a library backup in - added to what's here, games matched by store and name" };
            im.Click += (s, e) => ImportLibrary();
            lib.Children.Add(bk); lib.Children.Add(im); sp.Children.Add(lib);
            var emuBtn = new Button { Content = "Emulators...", Style = (Style)FindResource("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), ToolTip = "Your emulators and ROM folders - every ROM becomes a game" };
            emuBtn.Click += (s, e) => OpenEmulators();   // v2.9
            sp.Children.Add(emuBtn);
            var upd = new Button { Content = "Check for updates", Style = (Style)FindResource("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            upd.Click += (s, e) => CheckForUpdate(true); sp.Children.Add(upd);
            linkInfo = new TextBlock { Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), FontSize = 11.5, LineHeight = 18 };
            sp.Children.Add(linkInfo);
            sp.Children.Add(new TextBlock { Text = "Playtime counts for any game running from its install folder, however you start it (Steam, Battle.net, a shortcut).",
                Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontSize = 11.5 });
            sp.Children.Add(new TextBlock { Text = "Keys: arrows move, Enter plays, Ctrl+F search, Ctrl+R surprise me (Ctrl+Shift+R from your backlog), F5 refresh, F11 Big Picture, Esc back.\nController: d-pad or stick, A play, X surprise me, Y favorite, LB/RB filters, B back, Start stats, View Big Picture.\nTray icon: click for the quick launcher, double-click to open Hub.",
                Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontSize = 11.5, LineHeight = 18 });
            sp.Children.Add(new TextBlock { Text = "Hallow Hub " + Program.Version + "  -  icons: Font Awesome Free (CC BY 4.0)", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 12, 0, 0), FontSize = 11 });
            b.Child = sp; return AsPage(b, true);   // v2.28: a page under the tabs (it scrolls)
        }

        // ================================================================ v1.1 helpers
        // total playtime: Steam's own count or Hub's, whichever is bigger (Steam also counts the time Hub sees)
        double Total(Game g) { return Math.Max(S.For(g.Id).Minutes, g.SteamMinutes); }
        DateTime? LastOf(Game g)
        {
            var a = S.For(g.Id).Last; var b = g.LauncherLast;
            foreach (var x in g.Alts) if (x.LauncherLast != null && (b == null || x.LauncherLast > b)) b = x.LauncherLast;
            if (a != null) a = a.Value.ToUniversalTime();
            if (a == null) return b; if (b == null) return a; return a > b ? a : b;
        }
        // v2.1: when this copy of a merged game was last played (its launcher's record, or Hub seeing it run)
        DateTime? CopyLast(Game c)
        {
            var d = S.For(Main(c).Id); DateTime? b = c.LauncherLast; DateTime h;
            if (d.LastCopy == c.Id && DateTime.TryParse(d.LastCopyAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out h)) { h = h.ToUniversalTime(); if (b == null || h > b) b = h; }
            return b;
        }
        // a second copy you don't play: not the one Play starts, not running, and not played for 30 days
        Game UnusedCopy(Game g)
        {
            if (g.Alts.Count == 0) return null;
            var cur = PlayCopy(g);
            return new[] { g }.Concat(g.Alts).Where(c => c != cur && !tracker.Now.ContainsKey(c.Id) && (c.SizeBytes <= 0 || c.SizeBytes >= (256L << 20)))
                .Where(c => { var l = CopyLast(c); return l == null || (DateTime.UtcNow - l.Value).TotalDays > 30; }).OrderByDescending(c => c.SizeBytes).FirstOrDefault();
        }
        // the copy you actually use: the one played most recently, then the one with the most playtime
        Game UsedCopy(Game g)
        {
            if (g.Alts.Count == 0) return g;
            return new[] { g }.Concat(g.Alts).OrderByDescending(c => CopyLast(c) ?? DateTime.MinValue).ThenByDescending(c => c.SteamMinutes).ThenBy(c => c == g ? 0 : 1).First();
        }
        double Recent(Game g) { return Math.Max(S.For(g.Id).MinutesSince(DateTime.Today.AddDays(-13)), g.Steam2Weeks); }
        static Game Main(Game g) { return g.Parent ?? g; }
        IEnumerable<Game> AllCopies() { return games.Concat(games.SelectMany(g => g.Alts)); }
        Tracker.Running RunningOf(Game g)
        {
            Tracker.Running r; if (tracker.Now.TryGetValue(g.Id, out r)) return r;
            foreach (var a in g.Alts) if (tracker.Now.TryGetValue(a.Id, out r)) return r;
            return null;
        }
        bool IsRunning(Game g) { return RunningOf(g) != null; }
        Game PlayCopy(Game g)
        {
            var want = S.For(g.Id).PlayOn;   // you picked one (Play on) - otherwise the copy you actually use
            var picked = string.IsNullOrEmpty(want) ? null : g.Id == want ? g : g.Alts.FirstOrDefault(a => a.Id == want);
            return picked ?? UsedCopy(g);
        }
        // started straight from its .exe (so launch options and run-as-admin apply)
        static bool ExeLaunch(Game c) { return c != null && File.Exists(c.Launch) && string.IsNullOrEmpty(c.Args) && c.Source != "battlenet"; }
        static string Size(long bytes) { return bytes <= 0 ? null : bytes >= (1L << 30) ? (bytes / (double)(1L << 30)).ToString("0") + " GB" : (bytes / (double)(1L << 20)).ToString("0") + " MB"; }

        // v2.15: is the logo mostly light (made for dark art)? then light mode shows the game's name instead
        static readonly Dictionary<string, bool> brightCache = new Dictionary<string, bool>();
        static bool BrightLogo(string path, BitmapSource src)
        {
            if (path == null || src == null) return false;
            bool r; if (brightCache.TryGetValue(path, out r)) return r;
            try
            {
                BitmapSource s = src; if (s.PixelWidth > 200) s = new TransformedBitmap(s, new ScaleTransform(200.0 / s.PixelWidth, 200.0 / s.PixelWidth));
                var fb = new FormatConvertedBitmap(s, PixelFormats.Bgra32, null, 0);
                int w = fb.PixelWidth, h = fb.PixelHeight; var px = new byte[w * h * 4]; fb.CopyPixels(px, w * 4, 0);
                double sum = 0, n = 0;
                for (int i = 0; i < px.Length; i += 4) { if (px[i + 3] < 128) continue; sum += (0.0722 * px[i] + 0.7152 * px[i + 1] + 0.2126 * px[i + 2]) / 255.0; n++; }
                r = n > 0 && sum / n > 0.55;
            }
            catch { r = false; }
            brightCache[path] = r; return r;
        }
        // the cover's own colour, for the glow around the selected game
        static readonly Dictionary<string, Color> accentCache = new Dictionary<string, Color>();
        static Color Accent(Game g)
        {
            var orange = C("#F07820");
            var path = g.Cover ?? g.Header; if (path == null || !File.Exists(path)) return orange;
            Color c; if (accentCache.TryGetValue(path, out c)) return c;
            try
            {
                var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(path); bi.DecodePixelWidth = 24; bi.EndInit();
                var fb = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
                int w = fb.PixelWidth, h = fb.PixelHeight; var px = new byte[w * h * 4]; fb.CopyPixels(px, w * 4, 0);
                double r = 0, gg = 0, b = 0, wt = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    double B0 = px[i] / 255.0, G0 = px[i + 1] / 255.0, R0 = px[i + 2] / 255.0;
                    double mx = Math.Max(R0, Math.Max(G0, B0)), mn = Math.Min(R0, Math.Min(G0, B0));
                    double sat = mx <= 0 ? 0 : (mx - mn) / mx, k = sat * sat * mx;   // vivid, bright pixels count most
                    r += R0 * k; gg += G0 * k; b += B0 * k; wt += k;
                }
                if (wt < 0.5) c = orange;
                else
                {
                    r /= wt; gg /= wt; b /= wt; double m = Math.Max(r, Math.Max(gg, b)); if (m < 0.75) { r *= 0.75 / m; gg *= 0.75 / m; b *= 0.75 / m; }   // keep it bright enough to glow
                    c = Color.FromRgb((byte)Math.Min(255, r * 255), (byte)Math.Min(255, gg * 255), (byte)Math.Min(255, b * 255));
                }
            }
            catch { c = orange; }
            accentCache[path] = c; return c;
        }

        // ================================================================ data -> screen
        // v1.5: scans off the window's thread (watchers fire often while Steam downloads)
        bool rescanning;
        void RescanInBackground(Action after)
        {
            if (rescanning) return; rescanning = true;
            var custom = S.Custom.ToList(); var emus = S.Emulators.ToList();
            new Thread(() =>
            {
                List<Game> found = null; try { found = Library.Scan(custom, emus); } catch { }
                Dispatcher.BeginInvoke(new Action(() => { rescanning = false; if (found != null) { Apply(found); if (after != null) after(); } }));
            }) { IsBackground = true }.Start();
        }
        void Rescan() { Apply(Library.Scan(S.Custom, S.Emulators)); }
        void Apply(List<Game> found)
        {
            games = found;
            foreach (var g in AllCopies())
            {
                g.OriginalName = g.Name;
                var d = S.For(Main(g).Id);
                if (!string.IsNullOrEmpty(d.CustomName)) g.Name = d.CustomName;
                if (!string.IsNullOrEmpty(d.CustomCover) && File.Exists(d.CustomCover)) g.Cover = d.CustomCover;
                if (!string.IsNullOrEmpty(d.CustomHero) && File.Exists(d.CustomHero)) g.Hero = d.CustomHero;
            }
            // games without art (Battle.net, Epic, added) borrow Steam's artwork when the same game is on Steam
            foreach (var g in games.Where(x => x.Cover == null))
            {
                var twin = AllCopies().FirstOrDefault(x => x.Source == "steam" && x.Cover != null && Library.Norm(x.Name) == Library.Norm(g.Name));
                if (twin != null) { g.Cover = twin.Cover; g.Hero = twin.Hero; g.Logo = twin.Logo; g.Header = twin.Header; }
            }
            ApplyKnownSizes(); SizeMergedCopies();
            if (sel != null) sel = games.FirstOrDefault(x => x.Id == sel.Id);
            if (sel == null) sel = games.FirstOrDefault(x => x.Id == S.Selected);
            if (sel == null) sel = Sorted(games.Where(x => !S.For(x.Id).Hidden)).FirstOrDefault();
            RebuildChips(); RebuildGrid(); ShowHero(); UpdateStatus();
        }

        IEnumerable<Game> Sorted(IEnumerable<Game> list) { return SortedInner(list).OrderByDescending(g => S.For(g.Id).Pinned); }   // stable: pins first, rest as sorted
        IEnumerable<Game> SortedInner(IEnumerable<Game> list)
        {
            if (S.Sort == "played") return list.OrderByDescending(g => Total(g)).ThenBy(g => g.Name);
            if (S.Sort == "name") return list.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase);
            if (S.Sort == "rating") return list.OrderByDescending(g => S.For(g.Id).Rating).ThenByDescending(g => Total(g)).ThenBy(g => g.Name);
            if (S.Sort == "status") return list.OrderBy(g => StatusOrder(S.For(g.Id).Status)).ThenByDescending(g => LastOf(g) ?? DateTime.MinValue).ThenBy(g => g.Name);
            if (S.Sort == "store") return list.OrderBy(g => g.SourceName, StringComparer.CurrentCultureIgnoreCase).ThenByDescending(g => LastOf(g) ?? DateTime.MinValue).ThenBy(g => g.Name);
            if (S.Sort == "size") return list.OrderByDescending(g => g.SizeBytes + g.Alts.Sum(a => a.SizeBytes)).ThenBy(g => g.Name);
            return list.OrderByDescending(g => LastOf(g) ?? DateTime.MinValue).ThenByDescending(g => Total(g)).ThenBy(g => g.Name);
        }

        void RebuildChips()
        {
            chips.Children.Clear();
            var opts = new List<Tuple<string, string>> { Tuple.Create("all", "All"), Tuple.Create("fav", "Favorites") };
            if (games.Any(g => g.NeedsUpdate || g.Updating)) opts.Add(Tuple.Create("upd", "Needs update"));
            if (games.Any(Stale)) opts.Add(Tuple.Create("stale", "Not played lately"));
            foreach (var src in new[] { "steam", "battlenet", "epic", "ea", "ubisoft", "gog", "riot", "xbox", "custom", "rom" })
                if (games.Any(g => g.Source == src)) opts.Add(Tuple.Create(src, games.First(g => g.Source == src).SourceName));
            foreach (var c in S.Collections) if (games.Any(g => S.For(g.Id).Tags.Contains(c))) opts.Add(Tuple.Create("col:" + c, c));
            int hiddenN = HiddenCount(); if (hiddenN > 0) opts.Add(Tuple.Create("hidden", "Hidden (" + hiddenN + ")"));   // v2.3: where hidden games are, to bring them back
            if (!opts.Any(o => o.Item1 == S.Filter)) S.Filter = "all";
            foreach (var o in opts)
            {
                var key = o.Item1;
                object content = new TextBlock { Text = o.Item2 };   // v2.28: words only - quieter
                if (key.StartsWith("col:"))
                {
                    var cp = new StackPanel { Orientation = Orientation.Horizontal };
                    var ti = new System.Windows.Shapes.Path { Data = Icons.Get("tag"), Stretch = Stretch.Uniform, Width = 12, Height = 12, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
                    cp.Children.Add(ti); cp.Children.Add(new TextBlock { Text = o.Item2, VerticalAlignment = VerticalAlignment.Center }); content = cp; ti.Width = 10; ti.Height = 10; ti.Margin = new Thickness(0, 0, 6, 0);
                }
                var c = new ToggleButton { Content = content, Style = (Style)FindResource("Pill"), Margin = new Thickness(0, 0, 4, 4), IsChecked = S.Filter == key };
                if (key.StartsWith("col:")) ((System.Windows.Shapes.Path)((StackPanel)content).Children[0]).SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding("Foreground") { Source = c });
                c.Click += (s, e) => { S.Filter = key; S.Save(); foreach (ToggleButton x in chips.Children) x.IsChecked = x == c; RebuildGrid(); };
                chips.Children.Add(c);
            }
            loading = true;
            foreach (ComboBoxItem it in sortBox.Items) if ((string)it.Tag == S.Sort) sortBox.SelectedItem = it;
            loading = false;
        }

        List<Game> Visible()
        {
            var q = (search == null ? "" : search.Text ?? "").Trim();
            return Sorted(games.Where(g =>
                (S.Filter == "hidden" ? S.For(g.Id).Hidden : (S.ShowHidden || !S.For(g.Id).Hidden)) && PassesFilters(g) &&
                (S.Filter == "all" || S.Filter == "hidden" || (S.Filter == "fav" ? S.For(g.Id).Favorite : S.Filter == "upd" ? (g.NeedsUpdate || g.Updating) : S.Filter == "stale" ? Stale(g) : S.Filter.StartsWith("col:") ? S.For(g.Id).Tags.Contains(S.Filter.Substring(4)) : g.Source == S.Filter)) &&
                SearchMatch(g, q))).ToList();   // v2.10: names, genres, developer, notes, collections, status
        }

        void RebuildGrid()
        {
            if (grid == null) return;
            grid.Children.Clear(); cards.Clear();
            var list = Visible();
            foreach (var g in list) { var c = CardFor(g); cards[g.Id] = c; grid.Children.Add(c); }
            if (list.Count == 0)
                grid.Children.Add(new TextBlock { Text = games.Count == 0 ? "No games found yet. Click Add game to add one by its .exe." : (FilterCount() > 0 ? "No games match your filters." : "No games match."), Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(10, 20, 0, 0), FontSize = 14 });
            var shownOf = FilterCount() > 0 ? " of " + games.Count(x => S.Filter == "hidden" || S.ShowHidden || !S.For(x.Id).Hidden) : "";
            countText.Text = (list.Count == 1 && shownOf == "" ? "1 game" : list.Count + shownOf + " games"); UpdateFilterButton();
            // continue playing: the 4 most recent, on the main view only
            contRow.Children.Clear();
            var recent = (S.Filter == "all" && search.Text.Trim().Length == 0) ? games.Where(g => !S.For(g.Id).Hidden && LastOf(g) != null).OrderByDescending(g => LastOf(g)).Take(4).ToList() : new List<Game>();
            foreach (var g in recent) contRow.Children.Add(Wide(g));
            var show = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            contLabel.Visibility = show; contRow.Visibility = show; libLabel.Visibility = show;
            RebuildSide();   // v2.28: the game list on the left
        }

        static readonly Dictionary<string, ImageSource> imgCache = new Dictionary<string, ImageSource>();
        static ImageSource Img(string path, int decodeWidth)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var key = path + "|" + decodeWidth; ImageSource s;
            if (imgCache.TryGetValue(key, out s)) return s;
            try
            {
                var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(path);
                if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
                bi.EndInit(); bi.Freeze(); s = bi;
            }
            catch { s = null; }
            imgCache[key] = s; return s;
        }
        static ImageSource ExeIcon(string exe)
        {
            try { if (exe == null || !File.Exists(exe)) return null; var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe); var bs = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); bs.Freeze(); return bs; }
            catch { return null; }
        }

        Border Card(Game g)
        {
            var d = S.For(g.Id);
            // v2.28: a clean capsule - 2:3, small corners, no frame; under the mouse it lifts and shows the playtime
            var card = new Border { Width = 160 * K, Height = 240 * K, Margin = new Thickness(8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent, Background = (Brush)FindResource("Panel"), Cursor = Cursors.Hand, Tag = g,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1), ToolTip = g.Name };
            var inner = new Grid { Clip = new RectangleGeometry(new Rect(0, 0, 160 * K - 4, 240 * K - 4), 6, 6) };
            var cover = Img(g.Cover, K > 1.1 ? 420 : 320);
            if (cover != null) inner.Children.Add(new Image { Source = cover, Stretch = Stretch.UniformToFill });
            else
            {
                var col = C(SourceColor.ContainsKey(g.Source) ? SourceColor[g.Source] : "#A98BE0");
                var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                bg.GradientStops.Add(new GradientStop(Color.FromArgb(120, col.R, col.G, col.B), 0)); bg.GradientStops.Add(new GradientStop(C("#1C1724"), 0.8));
                inner.Children.Add(new Rectangle { Fill = bg });
                var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 30) };
                var ic = ExeIcon(g.IconExe);
                if (ic != null) st.Children.Add(new Image { Source = ic, Width = 56, Height = 56, Margin = new Thickness(0, 0, 0, 12) });
                st.Children.Add(new TextBlock { Text = g.Name, FontSize = 15, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = HallowLook.Light ? (Brush)FindResource("Text") : Brushes.White });
                inner.Children.Add(st);
            }
            var over = new Grid { Tag = "hover", Opacity = 0, IsHitTestVisible = false };   // shown under the mouse and on the selected game
            over.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)) });
            var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0)); shade.GradientStops.Add(new GradientStop(Color.FromArgb(215, 0x10, 0x0C, 0x16), 1));
            over.Children.Add(new Rectangle { Fill = shade, Height = 64, VerticalAlignment = VerticalAlignment.Bottom });
            var foot = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(11, 0, 0, 10) };
            var clock = Icons.Make("clock", 11, BD("#D6D0E2")); clock.Margin = new Thickness(0, 0, 6, 0); foot.Children.Add(clock);
            foot.Children.Add(new TextBlock { Text = Total(g) < 1 ? "New" : HubSettings.Duration(Total(g)), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = BD("#ECE8F4") });
            over.Children.Add(foot);
            var badge = Icons.Make(SrcIcon(g), 12, new SolidColorBrush(Color.FromArgb(200, 0xEC, 0xE8, 0xF4)));
            badge.HorizontalAlignment = HorizontalAlignment.Right; badge.VerticalAlignment = VerticalAlignment.Bottom; badge.Margin = new Thickness(0, 0, 11, 11);
            over.Children.Add(badge);
            inner.Children.Add(over);
            if (d.Pinned)
            {
                inner.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = new SolidColorBrush(Color.FromArgb(200, 0x16, 0x12, 0x1C)),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, d.Favorite ? 38 : 8, 0), Child = Icons.Make("thumbtack", 12, BD("#F07820")), ToolTip = "Pinned" });
            }
            if (d.Favorite)
            {
                var star = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = new SolidColorBrush(Color.FromArgb(200, 0x16, 0x12, 0x1C)),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0), Child = Icons.Make("star", 12, BD("#F0BE5A")) };
                inner.Children.Add(star);
            }
            if (!IsRunning(g) && (g.NeedsUpdate || g.Updating))
            {
                var up = new Border { CornerRadius = new CornerRadius(9), Background = B(g.Updating ? "#5AB4F0" : "#F0BE5A"), Padding = new Thickness(8, 2, 8, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 8, 0, 0),
                    Child = new TextBlock { Text = g.Updating ? (g.Progress >= 0 ? "UPDATING " + (int)(g.Progress * 100) + "%" : "UPDATING") : "UPDATE", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = B("#241A04") }, ToolTip = g.Updating ? "Steam is updating it" : "Steam has an update waiting - it downloads when you press Play" };
                inner.Children.Add(up);
            }
            if (IsRunning(g))
            {
                var pill = new Border { CornerRadius = new CornerRadius(9), Background = B("#6ED28C"), Padding = new Thickness(8, 2, 8, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 8, 0, 0),
                    Child = new TextBlock { Text = "PLAYING", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = B("#0E2416") } };
                inner.Children.Add(pill);
            }
            if (d.Hidden) inner.Opacity = 0.45;
            card.Child = inner;
            Paint(card, false);
            card.MouseEnter += (s, e) => { Zoom(card, 1.03); Paint(card, true); };
            card.MouseLeave += (s, e) => { Zoom(card, 1.0); Paint(card, false); };
            card.MouseLeftButtonUp += (s, e) => Open(g);
            card.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { Select(g); Play(g); } };
            card.ContextMenu = Menu(g);
            return card;
        }
        // v2.28: a quiet section title (the icon is kept for the call sites, the text says enough)
        FrameworkElement SectionLabel(string text, string icon)
        {
            return new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 6, 0, 10) };
        }
        // a wide "continue playing" card: the game's header art, when you last played and for how long (v2.28: the shelf's four
        // cards share its width, the text sits under the art)
        Border Wide(Game g)
        {
            var w = new Border { Height = 156, Margin = new Thickness(8, 0, 8, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1), ToolTip = g.Name };
            var gi = new Grid();
            gi.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); gi.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var pic = new Border { CornerRadius = new CornerRadius(8), Background = (Brush)FindResource("Panel") };
            var pg = new Grid();
            pic.SizeChanged += (s, e) => pg.Clip = new RectangleGeometry(new Rect(0, 0, pic.ActualWidth, pic.ActualHeight), 8, 8);
            var art = Img(g.Header, 520) ?? Img(g.Hero, 520) ?? Img(g.Cover, 320);
            if (art != null) pg.Children.Add(new Image { Source = art, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Center });
            else pg.Children.Add(new TextBlock { Text = g.Name, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap });
            var veil = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)), Opacity = 0, IsHitTestVisible = false };
            pg.Children.Add(veil);
            var play = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = BF("#F07820"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 10, 10), Child = Icons.Make("play", 13, B("#1A0E04")), Opacity = 0, ToolTip = "Play " + g.Name };
            ((FrameworkElement)play.Child).Margin = new Thickness(3, 0, 0, 0);
            play.MouseLeftButtonUp += (s, e) => { e.Handled = true; Select(g); Play(g); };
            pg.Children.Add(play);
            pic.Child = pg; gi.Children.Add(pic);
            var foot = new DockPanel { Margin = new Thickness(2, 7, 2, 0) };
            var running = IsRunning(g);
            var right = new TextBlock { Text = HubSettings.Duration(Total(g)), FontSize = 11.5, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(8, 0, 0, 0) };
            var left = new TextBlock { Text = running ? "Playing now" : HubSettings.Ago(LastOf(g)), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = running ? B("#6ED28C") : (Brush)FindResource("Text"), TextTrimming = TextTrimming.CharacterEllipsis };
            DockPanel.SetDock(right, Dock.Right); foot.Children.Add(right); foot.Children.Add(left);
            Grid.SetRow(foot, 1); gi.Children.Add(foot);
            w.Child = gi;
            w.MouseEnter += (s, e) => { Zoom(w, 1.03); veil.Opacity = 1; play.Opacity = 1; };
            w.MouseLeave += (s, e) => { Zoom(w, 1.0); veil.Opacity = 0; play.Opacity = 0; };
            w.MouseLeftButtonUp += (s, e) => Open(g);
            w.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { Select(g); Play(g); } };
            w.ContextMenu = Menu(g);
            return w;
        }
        // v2.28: the selected game gets a thin outline in the text colour (no glow); under the mouse a capsule brightens and shows its playtime
        void Paint(Border card, bool hot)
        {
            var g = card.Tag as Game;
            card.BorderBrush = g == sel ? B("#ECE8F4") : Brushes.Transparent;
            card.Effect = null;
            var inner = card.Child as Panel;
            bool capsule = false;
            if (inner != null) foreach (var c in inner.Children.OfType<FrameworkElement>()) if ((c.Tag as string) == "hover") { c.Opacity = hot || g == sel ? 1 : 0; capsule = true; }
            if (!capsule && hot && g != sel) card.BorderBrush = B("#3A3248");   // list rows
        }
        static void Zoom(Border card, double to)
        {
            var st = card.RenderTransform as ScaleTransform; if (st == null) return;
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(130)) { EasingFunction = new QuadraticEase() };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a); st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        ContextMenu Menu(Game g)
        {
            var m = new ContextMenu();
            Func<string, string, Action, MenuItem> item = (text, icon, act) =>
            {
                var mi = new MenuItem { Header = text, Icon = Icons.Make(icon, 13, B("#ECE8F4")) }; mi.Click += (s, e) => act(); m.Items.Add(mi); return mi;
            };
            item("Play", "play", () => Play(g));
            foreach (var alt in new[] { g }.Concat(g.Alts).Where(x => g.Alts.Count > 0))
            {
                var a = alt; item("Play on " + a.SourceName, a.Source == "steam" ? "steam" : a.Source == "battlenet" ? "battle-net" : "gamepad",
                    () => { S.For(g.Id).PlayOn = a.Id; S.Save(); Play(g); if (g == sel) ShowHero(); });
            }
            item(S.For(g.Id).Favorite ? "Remove from favorites" : "Add to favorites", "star", () => { var d = S.For(g.Id); d.Favorite = !d.Favorite; S.Save(); RefreshCard(g); if (g == sel) ShowHero(); });
            item(S.For(g.Id).Pinned ? "Unpin" : "Pin to the top", "thumbtack", () => { var d = S.For(g.Id); d.Pinned = !d.Pinned; S.Save(); RebuildGrid(); Status(d.Pinned ? g.Name + " is pinned to the top." : g.Name + " is unpinned."); });
            item("Game settings and collections...", "sliders", () => { Select(g); GameSettings(g); });
            item("Open folder", "folder-open", () => OpenFolder(g));
            item("Create desktop shortcut", "arrow-up-right-from-square", () => Shortcut(g));
            foreach (var cp in new[] { g }.Concat(g.Alts).Where(x => x.Source != "rom")) { var c2 = cp; item(g.Alts.Count > 0 ? "Uninstall the " + c2.SourceName + " copy..." : "Uninstall...", "trash-can", () => Uninstall(c2)); }
            if (g.Source == "steam") item("Steam store page", "arrow-up-right-from-square", () => OpenUrl("steam://store/" + g.Id.Substring(6)));
            m.Items.Add(LinksMenu(g));   // v2.12
            item(S.For(g.Id).Hidden ? "Unhide" : "Hide from the library", S.For(g.Id).Hidden ? "eye" : "eye-slash", () => ToggleHide(g));
            if (g.Source == "custom") item("Remove from Hallow Hub", "trash-can", () => { S.Custom.RemoveAll(x => x.Id == g.Id); S.Save(); Rescan(); Status(g.Name + " removed (its playtime is kept)."); });
            return m;
        }
        void RefreshCard(Game g)
        {
            Border old; if (g == null || !cards.TryGetValue(g.Id, out old)) return;
            int i = grid.Children.IndexOf(old); if (i < 0) return;
            var c = CardFor(g); cards[g.Id] = c; grid.Children.RemoveAt(i); grid.Children.Insert(i, c);
        }

        void Select(Game g)
        {
            if (g == null) return;
            var prev = sel; sel = g; S.Selected = g.Id; S.Save();
            Border b; if (prev != null && cards.TryGetValue(prev.Id, out b)) Paint(b, false);
            if (cards.TryGetValue(g.Id, out b)) Paint(b, b.IsMouseOver);
            PaintSideAll();   // v2.28: the game list
            ShowHero(); ShowPc();
        }

        void ShowHero()
        {
            if (heroImg == null) return;
            loading = true;
            try
            {
                var g = sel;
                if (g == null)
                {
                    heroImg.Source = null; heroLogo.Source = null; heroName.Text = "Your games show up here"; heroStats.Text = "Hallow Hub finds Steam, Battle.net and Epic Games games by itself. Add anything else with Add game."; heroStats.Visibility = Visibility.Visible;
                    playBtn.Visibility = Visibility.Collapsed; favBtn.Visibility = Visibility.Collapsed; heroPills.Children.Clear(); playOn.Children.Clear(); heroFacts.Children.Clear(); return;
                }
                playBtn.Visibility = Visibility.Visible; favBtn.Visibility = Visibility.Visible; heroStats.Visibility = Visibility.Collapsed;
                var d = S.For(g.Id);
                heroImg.Source = Img(g.Hero, 1600) ?? Img(g.Header, 0) ?? Img(g.Cover, 0);
                heroImg.Effect = g.Hero == null && heroImg.Source != null ? new System.Windows.Media.Effects.BlurEffect { Radius = 30 } : null;
                heroLogo.Source = Img(g.Logo, 0);
                if (HallowLook.Light && heroLogo.Source != null && BrightLogo(g.Logo, heroLogo.Source as BitmapSource)) heroLogo.Source = null;   // v2.15: a white logo on the light banner - the name instead
                heroLogo.Visibility = heroLogo.Source != null ? Visibility.Visible : Visibility.Collapsed;
                heroName.Text = g.Name; heroName.Visibility = heroLogo.Source != null ? Visibility.Collapsed : Visibility.Visible;
                var total = Total(g); var recent = Recent(g);
                heroStats.Text = (total < 1 ? "Not played yet" : HubSettings.Duration(total) + " played") +
                    (recent >= 1 ? "   \u00B7   " + HubSettings.Duration(recent) + " in the last 2 weeks" : "") +
                    "   \u00B7   Last played: " + HubSettings.Ago(LastOf(g));
                // v2.28: the facts next to Play, as small labelled columns
                heroFacts.Children.Clear();
                Action<string, string> fact = (label, value) =>
                {
                    var fsp = new StackPanel { Margin = new Thickness(0, 2, 30, 2) };
                    fsp.Children.Add(new TextBlock { Text = label, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Muted") });
                    fsp.Children.Add(new TextBlock { Text = value, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0) });
                    heroFacts.Children.Add(fsp);
                };
                fact("LAST PLAYED", IsRunning(g) ? "Playing now" : HubSettings.Ago(LastOf(g)));
                fact("PLAY TIME", total < 1 ? "Not played yet" : HubSettings.Duration(total));
                if (recent >= 1) fact("LAST 2 WEEKS", HubSettings.Duration(recent));
                heroPills.Children.Clear();
                Action<string, string, string> pill = (icon, text, col) =>
                {
                    var sp2 = new StackPanel { Orientation = Orientation.Horizontal };
                    if (icon != null) { var ic = Icons.Make(icon, 11, B(col)); ic.Margin = new Thickness(0, 0, 6, 0); sp2.Children.Add(ic); }
                    sp2.Children.Add(new TextBlock { Text = text, FontSize = 11.5, Foreground = B(col), FontWeight = FontWeights.SemiBold });
                    heroPills.Children.Add(new Border { Child = sp2, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 9, 4), Margin = new Thickness(0, 0, 6, 6),
                        Background = new SolidColorBrush(HallowLook.Light ? Color.FromArgb(200, 0xFF, 0xFF, 0xFF) : Color.FromArgb(140, 0x16, 0x12, 0x1C)) });   // v2.28: no outline
                };
                var srcIcon = g.Source == "steam" ? "steam" : g.Source == "battlenet" ? "battle-net" : g.Source == "custom" ? "windows" : g.Source == "rom" ? "compact-disc" : g.Source == "xbox" ? "xbox" : "gamepad";
                pill(srcIcon, (g.Source == "rom" && !string.IsNullOrEmpty(g.Platform) ? g.Platform : g.SourceName) + (g.Alts.Count > 0 ? " + " + string.Join(" + ", g.Alts.Select(a => a.SourceName)) : ""), "#ECE8F4");
                if (g.Updating) pill("arrows-rotate", g.Progress >= 0 ? "Updating " + (int)(g.Progress * 100) + "%" : "Updating", "#8FCBF5");
                else if (g.NeedsUpdate) pill("arrows-rotate", "Update waiting", "#F0BE5A");
                var sz = Size(g.SizeBytes); if (sz != null) pill(null, sz + (g.Drive != null ? " on " + g.Drive : ""), "#CFC8DC");
                foreach (var tg in d.Tags) pill("tag", tg, "#C9B2F5");
                if ((d.LaunchArgs ?? "").Trim().Length > 0) pill("terminal", d.LaunchArgs.Trim().Length > 24 ? d.LaunchArgs.Trim().Substring(0, 24) + "..." : d.LaunchArgs.Trim(), "#CFC8DC");
                if (d.Admin && ExeLaunch(PlayCopy(g))) pill("shield-halved", "As admin", "#F0BE5A");
                playOn.Children.Clear();
                if (g.Alts.Count > 0)
                {
                    playOn.Children.Add(new TextBlock { Text = "Play on", Foreground = B("#CFC8DC"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                    var cur = PlayCopy(g);
                    foreach (var copy in new[] { g }.Concat(g.Alts))
                    {
                        var cp = copy;
                        var cl = CopyLast(copy);
                        var chip = new ToggleButton { Content = copy.SourceName, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 6, 0), IsChecked = copy == cur, Padding = new Thickness(10, 4, 10, 4),
                            ToolTip = (copy.Drive != null ? copy.Drive + "  " : "") + copy.InstallDir + "\nLast played on this copy: " + (cl == null ? "not that Hub knows" : AgoLow(cl)) +
                                (copy.SizeBytes > 0 ? "\n" + Size(copy.SizeBytes) : "") + (copy.UpdatedAt > DateTime.MinValue ? "\nUpdated " + copy.UpdatedAt.ToString("d MMM yyyy") : "") };
                        chip.Click += (o, e2) => { S.For(g.Id).PlayOn = cp.Id; S.Save(); ShowHero(); Status("Play starts " + g.Name + " on " + cp.SourceName + "."); };
                        playOn.Children.Add(chip);
                    }
                    // v2.1: a copy you don't use (not played for a month) - offer to free its space, through its own launcher
                    var spare = UnusedCopy(g);
                    if (spare != null)
                    {
                        var spareSz = Size(spare.SizeBytes);
                        var tip = new Button { Style = (Style)FindResource("Btn"), Padding = new Thickness(10, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0),
                            Content = Icons.WithText("broom", "Free " + (spareSz ?? "space") + ": " + spare.SourceName + " copy unused", 11, B("#F0BE5A")),
                            ToolTip = "The " + spare.SourceName + " copy" + (spare.Drive != null ? " on " + spare.Drive : "") + " hasn't been played " + (CopyLast(spare) == null ? "that Hub knows of" : "since " + CopyLast(spare).Value.ToLocalTime().ToString("d MMM yyyy")) +
                                " - you play the " + cur.SourceName + " one.\nClick to uninstall it through " + spare.SourceName + " (it asks first). Nothing is deleted by Hub." };
                        tip.Click += (o, e2) => Uninstall(spare);
                        playOn.Children.Add(tip);
                    }
                }
                favBtn.IsChecked = d.Favorite;   // v2.28: no coloured frame or glow round the banner - the art fades into the page
                if (d.Favorite) favBtn.Foreground = BI("#F0BE5A"); else favBtn.ClearValue(ForegroundProperty);
                favBtn.ToolTip = d.Favorite ? "A favorite - click to remove" : "Add to favorites";
                ShowNewsButton(g); if (newsPanel.Visibility == Visibility.Visible) FillNews();
                ShowAboutButton(g); if (aboutPanel.Visibility == Visibility.Visible) FillAbout();
                ShowStatus(g);
                hideBtn.Content = d.Hidden ? "Unhide" : "Hide"; hideBtn.ToolTip = d.Hidden ? "Bring it back to your library" : "Hide it from your library (tools, test servers...) - nothing is uninstalled";
                var nt = (d.Notes ?? "").Trim();
                notesBtn.ToolTip = nt.Length == 0 ? "Notes (keybinds, loadouts, codes)" : (nt.Length > 400 ? nt.Substring(0, 400) + "..." : nt);
                notesBtn.BorderBrush = nt.Length > 0 ? B("#F07820") : null; if (nt.Length == 0) notesBtn.ClearValue(BorderBrushProperty);
                var r = RunningOf(g);
                if (r != null)
                {
                    var mins = (DateTime.Now - r.Since).TotalMinutes;
                    playBtn.Content = "Playing  \u00B7  " + (mins < 1 ? "just started" : HubSettings.Duration(mins));
                    playBtn.Background = B("#6ED28C"); playBtn.BorderBrush = B("#8EE0A6"); playBtn.ToolTip = "It's running - have fun"; if (HallowLook.Light) playBtn.Foreground = Brushes.White;
                }
                else
                {
                    playBtn.ClearValue(BackgroundProperty); playBtn.ClearValue(Border.BorderBrushProperty); playBtn.ClearValue(BorderBrushProperty); playBtn.ClearValue(ForegroundProperty);
                    playBtn.Content = starting == g.Id ? "Starting..." : "Play"; playBtn.ToolTip = "Start " + g.Name + " (double-click a game also plays it)";
                }
                // mixer preset
                presetBox.Items.Clear();
                presetBox.Items.Add(new ComboBoxItem { Content = presets == null ? "Hallow Mixer isn't running" : "Leave the mixer as it is", Tag = "" });
                var list = presets ?? new List<string>();
                if (d.Preset != "" && !list.Contains(d.Preset)) list = list.Concat(new[] { d.Preset }).ToList();
                foreach (var p in list) presetBox.Items.Add(new ComboBoxItem { Content = p, Tag = p });
                presetBox.SelectedItem = presetBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == d.Preset) ?? presetBox.Items[0];
                presetBox.IsEnabled = presets != null || d.Preset != "";
                // toolkit session
                bool can = Toolkit.CanSession;
                sessionBox.IsChecked = d.GameSession; sessionBox.IsEnabled = can;
                sessionNote.Text = can ? "Closes your Boost apps and uses High performance power while it runs - all back after." :
                    Toolkit.Script == null ? "Needs Hallow Toolkit." : "Needs Hallow Toolkit 5.2 or newer.";
                ShowChecklist(g, d);   // v2.12: what else happens (apps closed and opened)
            }
            finally { loading = false; }
            ShowStartSection();   // v2.28
            ShowFamily(false);
        }

        void CheckLinks()
        {
            var was = presets == null ? "" : string.Join("|", presets);
            presets = Mixer.Presets();
            var now = presets == null ? "" : string.Join("|", presets);
            if (was != now || (presets == null) != (was == "")) ShowHero();
            if (linkInfo != null)
                linkInfo.Text = "Hallow Mixer: " + (presets == null ? "not running (start it to pick presets per game)" : "connected - " + presets.Count + " presets") + "\n" +
                    "Hallow Toolkit: " + (Toolkit.Script == null ? "not installed" : Toolkit.CanSession ? "found - game sessions available" : "found - update to 5.2 for game sessions") +
                    FamilyAppLines();   // v2.13: Hallow Guard and Hallow Assist, when installed
            UpdateStatus();
        }

        void UpdateStatus()
        {
            if (statusText == null) return;
            // v2.28: the bottom bar - today's playtime on the right, what's going on (playing, updates) on the left
            var week = S.Games.Values.Sum(x => x.MinutesSince(DateTime.Today.AddDays(-6)));
            var today = S.Games.Values.Sum(x => x.MinutesSince(DateTime.Today));
            if (todayText != null) todayText.Text = "Today " + (today < 1 ? "0 min" : HubSettings.Duration(today)) + "   \u00B7   This week " + (week < 1 ? "0 min" : HubSettings.Duration(week)) + "   \u00B7   " + games.Count + " games installed";
            var upd = games.Where(x => x.Updating).ToList(); int waiting = games.Count(x => x.NeedsUpdate && !x.Updating);
            if (dlBox != null)
            {
                dlBox.Visibility = upd.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                var pr = upd.Where(x => x.Progress >= 0).Select(x => x.Progress).DefaultIfEmpty(0).First(); dlFill.Width = 90 * Math.Max(0, Math.Min(1, pr));
            }
            if (statusNote != null && DateTime.Now < statusUntil) return;
            var run = tracker.Now.Values.Select(r => Main(r.Game).Name).Distinct().ToList();
            var bits = new List<string>();
            if (run.Count > 0) bits.Add("Playing " + string.Join(", ", run));
            if (upd.Count > 0) bits.Add("Updating " + upd[0].Name + (upd[0].Progress >= 0 ? " " + (int)(upd[0].Progress * 100) + "%" : "") + (upd.Count > 1 ? " and " + (upd.Count - 1) + " more" : ""));
            if (waiting > 0) bits.Add(waiting + (waiting == 1 ? " update waiting" : " updates waiting") + " - downloads when you press Play");
            statusText.Text = bits.Count > 0 ? string.Join("   \u00B7   ", bits) : "Ready";
        }
        string statusNote; DateTime statusUntil;
        void Status(string msg) { statusNote = msg; statusUntil = DateTime.Now.AddSeconds(8); statusText.Text = msg; if (dlBox != null) dlBox.Visibility = Visibility.Collapsed; }

        // ================================================================ actions
        void Play(Game g)
        {
            if (g == null) return;
            if (IsRunning(g)) { Status(g.Name + " is already running."); return; }
            try
            {
                var copy = PlayCopy(g); var d = S.For(g.Id); var extra = (d.LaunchArgs ?? "").Trim();
                ProcessStartInfo psi;
                if (copy.Source == "steam" && extra.Length > 0) psi = new ProcessStartInfo("steam://run/" + copy.Id.Substring(6) + "//" + Uri.EscapeDataString(extra) + "/") { UseShellExecute = true };
                else
                {
                    var exe = ExeLaunch(copy);
                    psi = new ProcessStartInfo(copy.Launch, ((copy.Args ?? "") + (exe && extra.Length > 0 ? " " + extra : "")).Trim()) { UseShellExecute = true };
                    if (File.Exists(copy.Launch)) psi.WorkingDirectory = System.IO.Path.GetDirectoryName(copy.Launch);
                    if (exe && d.Admin) psi.Verb = "runas";
                }
                try { var started = Process.Start(psi); if (copy.Source == "rom" && started != null) tracker.Pids[copy.Id] = started.Id; }   // v2.9: an emulator counts for the ROM Hub started
                catch (System.ComponentModel.Win32Exception wx) { if (wx.NativeErrorCode == 1223) { Status("Cancelled - " + g.Name + " wasn't started."); return; } throw; }
                starting = g.Id; startingAt = DateTime.Now; ShowHero();
                Status("Starting " + g.Name + (g.Alts.Count > 0 ? " on " + copy.SourceName : "") + "...");
            }
            catch (Exception ex) { Status("Couldn't start " + g.Name + ": " + ex.Message); }
        }

        void OnStarted(Game g)
        {
            var d = S.For(g.Id); d.Sessions++; d.LastPlayed = DateTime.Now.ToString("o"); StatusOnStart(g); S.Save();
            if (starting == g.Id) starting = null;
            var did = new List<string>();
            if (d.Preset != "" && Mixer.UsePreset(d.Preset)) did.Add("Hallow Mixer on \"" + d.Preset + "\"");
            if (d.GameSession && Toolkit.CanSession && sessionFor == null) { Toolkit.Session(true); sessionFor = g.Id; did.Add("game session on"); }
            did.AddRange(CloseBefore(g));   // v2.12: apps you asked to close for this game
            did.AddRange(PowerOnStart(g, d));   // v2.14: the power plan you picked
            OpenCompanions(g); ShareStart(g);
            Status(g.Name + " started" + (did.Count > 0 ? " - " + string.Join(", ", did) : "") + ".");
            RefreshCard(g); if (g == sel) ShowHero(); UpdateTrayMenu();
        }
        void OnStopped(Game g, DateTime since)
        {
            var d = S.For(g.Id); d.LastPlayed = DateTime.Now.ToString("o"); LogSession(g, since); S.Save();
            CloseCompanions(g);
            bool ended = false;
            if (sessionFor == g.Id) { Toolkit.Session(false); sessionFor = null; ended = true; }
            var back = ReopenAfter(g);   // v2.12
            var pw = PowerOnStop(g);   // v2.14
            Status(g.Name + " closed" + (ended ? " - game session ended, everything is back" : "") + (back.Count > 0 ? " - opened " + string.Join(", ", back) + " again" : "") + (pw != null ? " - " + pw : "") + (d.Minutes >= 1 ? ". " + HubSettings.Duration(d.Minutes) + " played in total." : "."));
            RefreshCard(g); if (g == sel) ShowHero(); UpdateTrayMenu();
        }

        void GameSettings(Game g)
        {
            if (g == null) return;
            var w = new GameSettingsWindow(g, S.For(g.Id), S, ExeLaunch(PlayCopy(g))) { Owner = this };
            if (w.ShowDialog() == true) { S.Save(); imgCache.Clear(); accentCache.Clear(); Rescan(); Status(g.Name + " settings saved."); }
        }

        void Reveal(Game g) { Border b; if (g != null && cards.TryGetValue(g.Id, out b)) b.BringIntoView(); }

        // ---- keyboard and controller: arrows / d-pad move, Enter / A play, Esc / B back
        void Move(int dx, int dy)
        {
            var list = Visible(); if (list.Count == 0) return;
            int i = sel == null ? -1 : list.IndexOf(sel);
            if (i < 0) { Select(list[0]); Reveal(list[0]); return; }
            int cols = S.View == "list" ? 1 : Math.Max(1, (int)((grid.ActualWidth + 1) / (160 * K + 16)));
            int j = Math.Max(0, Math.Min(list.Count - 1, i + dx + dy * cols));
            if (j != i) { Select(list[j]); Reveal(list[j]); }
        }
        bool Back()
        {
            if (newsPanel.Visibility == Visibility.Visible) { newsPanel.Visibility = Visibility.Collapsed; return true; }
            if (wishPanel.Visibility == Visibility.Visible) { wishPanel.Visibility = Visibility.Collapsed; return true; }
            if (aboutPanel.Visibility == Visibility.Visible) { aboutPanel.Visibility = Visibility.Collapsed; return true; }
            if (statsBtn.IsChecked == true) { statsBtn.IsChecked = false; return true; }
            if (storageBtn.IsChecked == true) { storageBtn.IsChecked = false; return true; }
            if (familyBtn.IsChecked == true) { familyBtn.IsChecked = false; return true; }
            if (gearBtn.IsChecked == true) { gearBtn.IsChecked = false; return true; }
            if (search.Text.Length > 0) { search.Text = ""; return true; }
            return false;
        }
        void NextFilter(int step)
        {
            var list = chips.Children.OfType<ToggleButton>().ToList(); if (list.Count == 0) return;
            int i = list.FindIndex(c => c.IsChecked == true); i = ((i < 0 ? 0 : i) + step + list.Count) % list.Count;
            list[i].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
        void OnKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var focus = Keyboard.FocusedElement;
            bool typing = focus is TextBox, inCombo = focus is ComboBox || focus is ComboBoxItem;
            if (ctrl && e.Key == Key.F) { search.Focus(); search.SelectAll(); e.Handled = true; return; }
            if (ctrl && e.Key == Key.R) { if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) Surprise("backlog"); else Surprise(); e.Handled = true; return; }
            if (e.Key == Key.F11) { OpenBigPicture(); e.Handled = true; return; }
            if (e.Key == Key.F5) { Rescan(); Status("Library refreshed - " + games.Count + " games."); e.Handled = true; return; }
            if (e.Key == Key.Escape) { if (Back()) { e.Handled = true; Keyboard.Focus(this); } return; }
            if (inCombo) return;
            if (typing && e.Key == Key.Down) { Keyboard.Focus(this); Move(0, 0); e.Handled = true; return; }
            if (typing) { if (e.Key == Key.Enter && sel != null) { Play(sel); e.Handled = true; } return; }
            switch (e.Key)
            {
                case Key.Left: Move(-1, 0); e.Handled = true; break;
                case Key.Right: Move(1, 0); e.Handled = true; break;
                case Key.Up: Move(0, -1); e.Handled = true; break;
                case Key.Down: Move(0, 1); e.Handled = true; break;
                case Key.Enter: if (sel != null) Play(sel); e.Handled = true; break;
            }
        }
        readonly Pad pad = new Pad(); bool padTold;
        void TickPad()
        {
            if (!IsActive || !IsVisible) return;   // never while you're in a game
            var b = pad.Poll();
            if (pad.Connected && !padTold) { padTold = true; Status("Controller connected - d-pad to move, A play, X surprise me, LB/RB filters, Y favorite, B back."); }
            if (b == 0) return;
            if ((b & Pad.Left) != 0) Move(-1, 0);
            if ((b & Pad.Right) != 0) Move(1, 0);
            if ((b & Pad.Up) != 0) Move(0, -1);
            if ((b & Pad.Down) != 0) Move(0, 1);
            if ((b & Pad.A) != 0 && sel != null) Play(sel);
            if ((b & Pad.B) != 0) Back();
            if ((b & Pad.X) != 0) Surprise();
            if ((b & Pad.Y) != 0 && sel != null) { var d = S.For(sel.Id); d.Favorite = !d.Favorite; S.Save(); RefreshCard(sel); ShowHero(); Status(sel.Name + (d.Favorite ? " added to" : " removed from") + " favorites."); }
            if ((b & Pad.LB) != 0) NextFilter(-1);
            if ((b & Pad.RB) != 0) NextFilter(1);
            if ((b & Pad.Start) != 0) statsBtn.IsChecked = statsBtn.IsChecked != true;
            if ((b & Pad.Back) != 0) OpenBigPicture();
        }

        // ---- v1.3: --play "<name or id>" (shortcuts, Stream Deck)
        public void PlayByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            var n = Library.Norm(key);
            var g = games.FirstOrDefault(x => string.Equals(x.Id, key, StringComparison.OrdinalIgnoreCase))
                ?? games.FirstOrDefault(x => Library.Norm(x.Name) == n)
                ?? games.FirstOrDefault(x => n.Length >= 3 && Library.Norm(x.Name).Contains(n));
            if (g == null) { Status("No game called \"" + key + "\" in your library."); if (tray != null) tray.ShowBalloonTip(3000, "Hallow Hub", "No game called \"" + key + "\" in your library.", WF.ToolTipIcon.None); return; }
            Select(g); Play(g);
        }
        // a desktop shortcut that starts the game through Hub (so its preset and game session apply)
        void Shortcut(Game g)
        {
            try
            {
                var name = new string(g.Name.Where(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) == false).ToArray()).Trim();
                var lnk = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name + ".lnk");
                var icon = (g.Source == "steam" ? Library.SteamIcon(g.Id.Substring(6)) : null) ?? g.IconExe ?? Assembly.GetExecutingAssembly().Location;
                var t = Type.GetTypeFromProgID("WScript.Shell"); var sh = Activator.CreateInstance(t);
                var sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk }); var st = sc.GetType();
                Action<string, object> set = (prop, v) => st.InvokeMember(prop, BindingFlags.SetProperty, null, sc, new[] { v });
                set("TargetPath", Assembly.GetExecutingAssembly().Location);
                set("Arguments", "--play \"" + g.Id + "\"");
                set("WorkingDirectory", System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
                set("IconLocation", icon + ",0");
                set("Description", "Play " + g.Name + " with Hallow Hub");
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                Marshal.FinalReleaseComObject(sc); Marshal.FinalReleaseComObject(sh);
                Status("Shortcut \"" + name + "\" is on your desktop - it starts the game through Hub.");
            }
            catch (Exception ex) { Status("Couldn't make the shortcut: " + ex.Message); }
        }

        // ---- v1.3: notice installs, uninstalls and Steam updates without a Refresh
        readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>(); DispatcherTimer rescanSoon;
        void StartWatching()
        {
            rescanSoon = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            rescanSoon.Tick += (s, e) => { rescanSoon.Stop(); var before = games.Count; RescanInBackground(() => { if (games.Count != before) Status(games.Count > before ? "New game found - your library is up to date." : "A game was uninstalled - your library is up to date."); }); };
            var dirs = Library.SteamLibraries().Select(l => Tuple.Create(System.IO.Path.Combine(l, "steamapps"), "appmanifest_*.acf")).ToList();
            dirs.Add(Tuple.Create(Library.EpicManifests, "*.item"));
            foreach (var d in dirs)
            {
                if (!Directory.Exists(d.Item1)) continue;
                try
                {
                    var w = new FileSystemWatcher(d.Item1, d.Item2) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                    FileSystemEventHandler h = (o, e) => Dispatcher.BeginInvoke(new Action(() => { rescanSoon.Stop(); rescanSoon.Start(); }));
                    w.Changed += h; w.Created += h; w.Deleted += h; w.Renamed += (o, e) => h(o, e);
                    w.EnableRaisingEvents = true; watchers.Add(w);
                }
                catch { }
            }
        }

        // ---- v1.3: updates from GitHub
        Button updateBtn; GhRelease pending;
        void CheckForUpdate(bool manual)
        {
            if (manual) Status("Checking GitHub for a newer Hallow Hub...");
            new Thread(() =>
            {
                var r = Updater.Latest();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    S.LastUpdateCheck = DateTime.Now.ToString("o"); S.Save();
                    if (r == null) { if (manual) Status("Couldn't reach GitHub - try again later."); return; }
                    if (!Updater.IsNewer(r)) { if (manual) Status("You have the newest version (v" + Program.Version + ")."); return; }
                    pending = r; updateBtn.Content = "Update to v" + r.Version.ToString(2); updateBtn.Visibility = Visibility.Visible;
                    updateBtn.ToolTip = (r.Notes ?? "").Length > 600 ? r.Notes.Substring(0, 600) + "..." : r.Notes;
                    Status("Hallow Hub v" + r.Version.ToString(2) + " is out - click Update at the top (your playtime and settings are kept).");
                }));
            }) { IsBackground = true }.Start();
        }
        void InstallUpdate()
        {
            if (pending == null) return;
            if (MessageBox.Show(this, "Install Hallow Hub v" + pending.Version.ToString(2) + " now?\n\nIt downloads from GitHub, checks the file, and reopens by itself. Your playtime and settings are kept.", "Update Hallow Hub", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            updateBtn.IsEnabled = false; updateBtn.Content = "Updating...";
            var r = pending; S.Save();
            new Thread(() =>
            {
                var err = Updater.Install(r);
                Dispatcher.BeginInvoke(new Action(() => { if (err != null) { Status(err); updateBtn.IsEnabled = true; updateBtn.Content = "Update to v" + r.Version.ToString(2); } else Status("Installing the update - Hallow Hub reopens in a few seconds."); }));
            }) { IsBackground = true }.Start();
        }
        void ShowWhatsNew()
        {
            if (S.LastSeenVersion == Program.Version || !IsVisible || Snap.On) return;
            var items = WhatsNew.Since(S.LastSeenVersion);
            S.LastSeenVersion = Program.Version; S.Save();
            if (items.Count > 0) new WhatsNewWindow(items) { Owner = this }.Show();
        }

        void AddGame()
        {
            var dlg = new OpenFileDialog { Title = "Pick the game's .exe", Filter = "Programs (*.exe)|*.exe", CheckFileExists = true };
            if (dlg.ShowDialog(this) != true) return;
            var g = Library.FromExe(dlg.FileName);
            if (games.Any(x => x.Id == g.Id)) { Status(g.Name + " is already in your library."); return; }
            S.Custom.Add(g); S.Save(); Rescan(); Select(games.FirstOrDefault(x => x.Id == g.Id)); Status(g.Name + " added.");
        }
        void OpenFolder(Game g)
        {
            if (g != null && g.RomPath != null && File.Exists(g.RomPath)) { try { Process.Start("explorer.exe", "/select,\"" + g.RomPath + "\""); } catch { } return; }   // v2.9
            if (g == null || string.IsNullOrEmpty(g.InstallDir) || !Directory.Exists(g.InstallDir)) { Status("Can't find that game's folder."); return; }
            try { Process.Start("explorer.exe", "\"" + g.InstallDir + "\""); } catch { }
        }
        void OpenUrl(string u) { try { Process.Start(new ProcessStartInfo(u) { UseShellExecute = true }); } catch { } }

        // ---- start with Windows: the normal per-user Run entry (Task Manager > Startup apps can turn it off too)
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        static bool StartsWithWindows() { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("HallowHub") != null; }
        void SetStartWithWindows(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("HallowHub", "\"" + Assembly.GetExecutingAssembly().Location + "\" --tray"); else k.DeleteValue("HallowHub", false);
                }
                Status(on ? "Hallow Hub starts with Windows (in the tray)." : "Hallow Hub won't start with Windows.");
            }
            catch (Exception ex) { Status("Couldn't change that: " + ex.Message); }
        }

        // ================================================================ tray
        WF.ToolStripMenuItem trayGames, quitItem;
        void SetupTray()
        {
            tray = new WF.NotifyIcon { Text = "Hallow Hub", Visible = true };
            try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { tray.Icon = System.Drawing.SystemIcons.Application; }
            var menu = new WF.ContextMenuStrip();
            var bigItem = new WF.ToolStripMenuItem("Big Picture (full screen)"); bigItem.Click += (s, e) => Dispatcher.BeginInvoke(new Action(OpenBigPicture));
            var open = new WF.ToolStripMenuItem("Open Hallow Hub"); open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
            open.Click += (s, e) => ShowFromTray(); menu.Items.Add(open);
            trayGames = new WF.ToolStripMenuItem("Play"); menu.Items.Add(trayGames); menu.Items.Add(bigItem);
            menu.Items.Add(new WF.ToolStripSeparator());
            var quit = quitItem = new WF.ToolStripMenuItem("Quit"); quit.Click += (s, e) => Quit(); menu.Items.Add(quit);
            menu.Opening += (s, e) => { UpdateTrayMenu(); UpdateFamilyMenu(menu); UpdateTraySteamMenu(menu); };   // v2.18: Steam account   // v2.4: the Hallow family's submenus
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) Dispatcher.BeginInvoke(new Action(ShowQuick)); };   // v2.0: quick launcher
            tray.MouseDoubleClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) Dispatcher.BeginInvoke(new Action(() => { if (quick != null) quick.Close(); ShowFromTray(); })); };
            UpdateTrayMenu();
        }
        void UpdateTrayMenu()
        {
            if (trayGames == null) return;
            trayGames.DropDownItems.Clear();
            foreach (var g in games.Where(x => !S.For(x.Id).Hidden).OrderByDescending(x => LastOf(x) ?? DateTime.MinValue).ThenByDescending(x => Total(x)).Take(8))
            {
                var gg = g; var mi = new WF.ToolStripMenuItem(g.Name + (IsRunning(g) ? "   (playing)" : ""));
                mi.Click += (s, e) => Dispatcher.BeginInvoke(new Action(() => Play(gg))); trayGames.DropDownItems.Add(mi);
            }
            trayGames.Enabled = trayGames.DropDownItems.Count > 0; WriteRecent();
            var run = tracker.Now.Values.Select(r => Main(r.Game).Name).FirstOrDefault();
            tray.Text = run != null ? ("Hallow Hub - playing " + run).Substring(0, Math.Min(63, ("Hallow Hub - playing " + run).Length)) : "Hallow Hub";
        }
        public void ShowFromTray()
        {
            Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate(); Topmost = true; Topmost = false; Focus();
        }
        void Quit()
        {
            exiting = true; S.Save(); WriteFamilyStatus(false); if (restarter != null) restarter.Save(true);
            if (sessionFor != null) { Toolkit.Session(false); sessionFor = null; }
            PowerRestoreNow(true);   // v2.14
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            Application.Current.Shutdown();
        }

        // ================================================================ developer snapshots (--snap <folder>)
        public void SnapAll(string dir)
        {
            // sample collection, in memory only (snapshots never save)
            if (S.Collections.Count == 0) { S.Collections.Add("With friends"); foreach (var g in games.Where(x => x.Name.StartsWith("Delta") || x.Name.StartsWith("Overwatch") || x.Name.StartsWith("Marvel"))) S.For(g.Id).Tags.Add("With friends"); }
            var df = games.FirstOrDefault(x => x.Name.StartsWith("Delta")); if (df != null) { S.For(df.Id).LaunchArgs = "-novid"; Select(df); }
            S.Filter = "all"; RebuildChips(); RebuildGrid(); ShowHero();
            Snap.Save(this, System.IO.Path.Combine(dir, "main.png"), 1180, 780);
            Snap.Save(this, System.IO.Path.Combine(dir, "main-wide.png"), 1600, 900);
            // v2.28: the game list opened on a small window, "When this game starts" opened, and what the More menus hold
            try
            {
                Snap.Save(this, System.IO.Path.Combine(dir, "main-narrow.png"), 960, 640); ToggleSidebar(); Snap.Save(this, System.IO.Path.Combine(dir, "main-narrow-list.png"), 960, 640); ToggleSidebar();
                S.StartOpen = true; ShowStartSection(); Snap.Save(this, System.IO.Path.Combine(dir, "main-start-open.png"), 1180, 1000); S.StartOpen = false; ShowStartSection();
                Snap.Save(this, System.IO.Path.Combine(dir, "main-tall.png"), 1180, 1400);
                Func<ItemsControl, string, string> dump = null;
                dump = (ic, ind) => string.Join("", ic.Items.Cast<object>().Select(o => { var mi = o as MenuItem; return mi == null ? ind + "----\r\n" : ind + mi.Header + "\r\n" + dump(mi, ind + "    "); }));
                File.WriteAllText(System.IO.Path.Combine(dir, "menus.txt"), "TOP BAR MORE\r\n" + dump(MoreMenu(), "  ") + "\r\nGAME MORE (" + sel.Name + ")\r\n" + dump(GameMenu(sel), "  ") +
                    "\r\nTABS: " + string.Join(" | ", new ButtonBase[] { libTab, familyBtn, statsBtn, storageBtn, wishBtn }.Select(x => x.Content + (x.Visibility == Visibility.Visible ? "" : " (hidden)"))) +
                    "\r\nGAME LIST: " + string.Join(" | ", sideList.Children.OfType<FrameworkElement>().Select(x => x is DockPanel ? "[" + string.Join(" ", ((DockPanel)x).Children.OfType<TextBlock>().Select(tb => tb.Text)) + "]" : ((x as Border) != null && (x as Border).Tag is Game ? ((Game)((Border)x).Tag).Name : "?"))) +
                    "\r\nBOTTOM: " + statusText.Text + "  ||  " + todayText.Text + "\r\nSTART SUMMARY: " + startSummary.Text + "\r\n");
            }
            catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "shell.err.txt"), ex.ToString()); }
            settingsPanel.Visibility = Visibility.Visible;
            Snap.Save(this, System.IO.Path.Combine(dir, "settings.png"), 1180, 780);
            try { var cf = Crash.Newest(); if (cf != null) Snap.Save(new CrashWindow(cf), System.IO.Path.Combine(dir, "crash.png"), 460, 0); } catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "crash.err.txt"), ex.ToString()); }
            try { File.WriteAllText(System.IO.Path.Combine(dir, "traymenu.txt"), FamilyMenuText()); } catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "traymenu.err.txt"), ex.ToString()); }
            // v2.13: the Hallow icon's menu as a picture, and Settings / the PC check with Hallow Guard and Hallow Assist
            try { SnapTrayMenu(System.IO.Path.Combine(dir, "traymenu.png")); } catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "traymenu.err.txt"), ex.ToString()); }
            try { CheckLinks(); Snap.Save(this, System.IO.Path.Combine(dir, "settings-213.png"), 1180, 900); File.WriteAllText(System.IO.Path.Combine(dir, "linkinfo.txt"), linkInfo.Text + "\r\ntray-host (icon showing): ok " + FamilyTray.HostJson(true)); } catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "settings213.err.txt"), ex.ToString()); }
            settingsPanel.Visibility = Visibility.Collapsed;
            FillStats(); statsPanel.Visibility = Visibility.Visible;
            Snap.Save(this, System.IO.Path.Combine(dir, "stats.png"), 1180, 780);
            Snap.Save(this, System.IO.Path.Combine(dir, "stats-narrow.png"), 960, 640);   // v2.28
            statsPanel.Visibility = Visibility.Collapsed;
            var keepFilter = S.Filter;
            OpenBigPicture(); if (big != null) { Snap.Save(big, System.IO.Path.Combine(dir, "big.png"), 1600, 900);
                bigSnapStep(1); Snap.Save(big, System.IO.Path.Combine(dir, "big-collection.png"), 1600, 900);
                bigSnapStep(2); Snap.Save(big, System.IO.Path.Combine(dir, "big-storage.png"), 1600, 900);
                bigSnapStep(3); Snap.Save(big, System.IO.Path.Combine(dir, "big-confirm.png"), 1600, 900); BsHide(); big = null; bsView = null; }
            S.Filter = keepFilter; RebuildChips(); RebuildGrid();
            ShowQuick(); if (quick != null) { Snap.Save(quick, System.IO.Path.Combine(dir, "quick.png"), 390, 0); quick = null; }
            S.View = "list"; RebuildGrid(); Snap.Save(this, System.IO.Path.Combine(dir, "list.png"), 1180, 900); S.View = "grid"; S.CardScale = 0.8; RebuildGrid(); Snap.Save(this, System.IO.Path.Combine(dir, "small.png"), 1180, 900); S.CardScale = 1; RebuildGrid();
            FillStorage(); storagePanel.Visibility = Visibility.Visible; Snap.Save(this, System.IO.Path.Combine(dir, "storage.png"), 1180, 900); storagePanel.Visibility = Visibility.Collapsed;
            try { var gw = new GameSettingsWindow(df ?? games[0], S.For((df ?? games[0]).Id), S, false); Snap.Save(gw, System.IO.Path.Combine(dir, "gamesettings.png"), 480, 0); } catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "gs.err.txt"), ex.ToString()); }
            Surprise(); Snap.Save(this, System.IO.Path.Combine(dir, "surprise.png"), 1180, 780);
            var ow = games.FirstOrDefault(g => g.Alts.Count > 0); if (ow != null) { Select(ow); Snap.Save(this, System.IO.Path.Combine(dir, "merged.png"), 1180, 780); }
            // v2.3: the filters pop-up (drawn on its own), filters in use, and a hidden game under Hidden
            try
            {
                S.FSize = "big"; S.FStores.Add("steam"); FillFilters(); var fpc = filterPop.Child; filterPop.Child = null;
                var fw = new Window { Background = (Brush)FindResource("Bg"), Foreground = Foreground, FontFamily = FontFamily, FontSize = FontSize, Content = fpc };
                Snap.Save(fw, System.IO.Path.Combine(dir, "filters.png"), 400, 0); fw.Content = null; filterPop.Child = fpc;
                RebuildGrid(); Snap.Save(this, System.IO.Path.Combine(dir, "filtered.png"), 1180, 780);
                S.FSize = ""; S.FStores.Clear();
                var hid = games.FirstOrDefault(x => x.Name.StartsWith("Lethal")) ?? games.Last();
                S.For(hid.Id).Hidden = true; S.Filter = "hidden"; RebuildChips(); RebuildGrid(); Select(hid); Snap.Save(this, System.IO.Path.Combine(dir, "hidden.png"), 1180, 780);
                S.For(hid.Id).Hidden = false; S.Filter = "all"; RebuildChips(); RebuildGrid();
            }
            catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "extras.err.txt"), ex.ToString()); }
            // v2.6: news (from the test cache) and the week / month charts with sample history (in memory only)
            try
            {
                if (df != null) { Select(df); OpenNews(); Snap.Save(this, System.IO.Path.Combine(dir, "news.png"), 1180, 900); newsPanel.Visibility = Visibility.Collapsed; }
                var rnd = new Random(7); var some = games.Take(4).ToList();
                for (int i = 0; i < 360; i++) { var day = DateTime.Today.AddDays(-i); if (rnd.Next(3) == 0) continue; var gg = some[rnd.Next(some.Count)]; S.For(gg.Id).AddMinutes(20 + rnd.Next(150), day); }
                statsMode = "weeks"; FillStats(); statsPanel.Visibility = Visibility.Visible; Snap.Save(this, System.IO.Path.Combine(dir, "stats-weeks.png"), 1180, 900);
                statsMode = "months"; FillStats(); Snap.Save(this, System.IO.Path.Combine(dir, "stats-months.png"), 1180, 900); statsPanel.Visibility = Visibility.Collapsed; statsMode = "days";
            }
            catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "news.err.txt"), ex.ToString()); }
            // v2.7: sale alerts (from the test wishlist cache), Settings with them on, and a library backup round trip
            try
            {
                S.SaleAlerts = true; S.SteamProfile = "76561197960265729"; S.SaleTargets["1966720"] = 12; UpdateWishButton();
                OpenWishlist(); Snap.Save(this, System.IO.Path.Combine(dir, "wishlist.png"), 1180, 900); wishPanel.Visibility = Visibility.Collapsed;
                var outer = (Grid)Content; outer.Children.Remove(settingsPanel); settingsPanel = BuildSettings(); outer.Children.Add(settingsPanel);
                settingsPanel.Visibility = Visibility.Visible; Snap.Save(this, System.IO.Path.Combine(dir, "settings-27.png"), 1180, 900); settingsPanel.Visibility = Visibility.Collapsed;
                var t = Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST");
                if (!string.IsNullOrEmpty(t))
                {
                    var zip = System.IO.Path.Combine(t, "library-test.zip");
                    var names = AllCopies().GroupBy(c => c.Id).ToDictionary(x => x.Key, x => x.First().OriginalName ?? x.First().Name);
                    S.For(games[0].Id).Notes = "test note"; S.For(games[0].Id).Tags.Add("Backup test");
                    int n = LibraryZip.Export(S, names, zip);
                    Dictionary<string, byte[]> art; var b = LibraryZip.Read(zip, out art);
                    var before = S.Collections.Count;
                    b.Names["steam:999999999"] = games[1].OriginalName ?? games[1].Name; b.Games["steam:999999999"] = new GameData { Notes = "matched by name", Pinned = true };
                    int m = MergeBackup(b, art, System.IO.Path.Combine(t, "art"));
                    var json = Encoding.UTF8.GetString(b.ToJson());
                    File.WriteAllText(System.IO.Path.Combine(dir, "backup.txt"), "exported " + n + " games, matched on import " + m + "\r\nentries in zip: " + string.Join(", ", System.IO.Compression.ZipFile.OpenRead(zip).Entries.Select(x => x.FullName)) +
                        "\r\nname-matched game notes: " + S.For(games[1].Id).Notes + " pinned=" + S.For(games[1].Id).Pinned + "\r\ncollections " + before + " -> " + S.Collections.Count +
                        "\r\njson mentions webhook/token/key: " + (json.IndexOf("webhook", StringComparison.OrdinalIgnoreCase) >= 0) + "/" + (json.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0) + "/" + (json.IndexOf("apikey", StringComparison.OrdinalIgnoreCase) >= 0) + "\r\nlength " + json.Length);
                }
            }
            catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "sales.err.txt"), ex.ToString()); }
            // v2.8: game details (from the test cache) and a genre filter
            try
            {
                var lc = games.FirstOrDefault(x => x.Id == "steam:1966720");
                if (lc != null)
                {
                    Select(lc); OpenAbout(); Snap.Save(this, System.IO.Path.Combine(dir, "about.png"), 1180, 900); aboutPanel.Visibility = Visibility.Collapsed;
                    S.FTags = new List<string> { "Co-op" }; FillFilters(); var fpc2 = filterPop.Child; filterPop.Child = null;
                    var fw2 = new Window { Background = (Brush)FindResource("Bg"), Foreground = Foreground, FontFamily = FontFamily, FontSize = FontSize, Content = fpc2 };
                    Snap.Save(fw2, System.IO.Path.Combine(dir, "filters-genre.png"), 400, 0); fw2.Content = null; filterPop.Child = fpc2;
                    RebuildGrid(); Snap.Save(this, System.IO.Path.Combine(dir, "filtered-genre.png"), 1180, 780); S.FTags.Clear(); RebuildGrid();
                }
            }
            catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "meta.err.txt"), ex.ToString()); }
            // v2.9: emulators - the profiles window and the ROMs from a test folder
            try
            {
                var t9 = Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST");
                if (!string.IsNullOrEmpty(t9) && Directory.Exists(System.IO.Path.Combine(t9, "roms")))
                {
                    S.Emulators = new List<Emulator> { new Emulator { Name = "Super Nintendo", Exe = System.IO.Path.Combine(t9, "emu", "snes9x.exe"), Args = "-fullscreen \"{rom}\"", Folder = System.IO.Path.Combine(t9, "roms"), Types = "sfc smc" } };
                    Snap.Save(new EmulatorsWindow(S.Emulators), System.IO.Path.Combine(dir, "emulators.png"), 640, 0);
                    Rescan(); S.Filter = "rom"; RebuildChips(); RebuildGrid(); var rg = games.FirstOrDefault(x => x.Source == "rom"); if (rg != null) Select(rg);
                    Snap.Save(this, System.IO.Path.Combine(dir, "roms.png"), 1180, 780);
                    File.WriteAllText(System.IO.Path.Combine(dir, "roms.txt"), string.Join("\r\n", games.Where(x => x.Source == "rom").Select(x => x.Name + " | " + x.Launch + " " + x.Args + " | cover=" + (x.Cover != null))));
                    S.Filter = "all"; RebuildChips(); RebuildGrid();
                }
            }
            catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "roms.err.txt"), ex.ToString()); }
            // v2.10: play status, rating and search
            try
            {
                foreach (var g in games) { S.For(g.Id).Status = ""; S.For(g.Id).Rating = 0; }
                var owg = games.FirstOrDefault(x => x.Alts.Count > 0) ?? games[0];
                S.For(owg.Id).Status = "playing"; S.For(owg.Id).Rating = 4;
                if (games.Count > 2) { S.For(games[1].Id).Status = "beaten"; S.For(games[1].Id).Rating = 5; S.For(games[2].Id).Status = "plan"; }
                S.Filter = "all"; RebuildChips(); Select(owg); RebuildGrid(); Snap.Save(this, System.IO.Path.Combine(dir, "status.png"), 1180, 780);
                var lines = new List<string>();
                foreach (var q in new[] { "lethal", "genre:co-op", "co-op", "status:playing", "status:plan", "dev:zeekerss", "note:test", "store:battle", "zzz nothing", "Half-Life: Alyx" })
                { search.Text = q; lines.Add(q + " -> " + string.Join(", ", Visible().Select(x => x.Name))); }
                search.Text = "genre:co-op"; Snap.Save(this, System.IO.Path.Combine(dir, "search.png"), 1180, 780); search.Text = "";
                S.FStatus = "playing"; RebuildGrid(); lines.Add("filter playing -> " + string.Join(", ", Visible().Select(x => x.Name))); S.FStatus = "";
                S.Sort = "rating"; lines.Add("sort rating -> " + string.Join(", ", Visible().Take(3).Select(x => x.Name))); S.Sort = "recent";
                S.View = "list"; RebuildGrid(); Snap.Save(this, System.IO.Path.Combine(dir, "status-list.png"), 1180, 780); S.View = "grid"; RebuildGrid();
                File.WriteAllText(System.IO.Path.Combine(dir, "search.txt"), string.Join("\r\n", lines));
            }
            catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "status.err.txt"), ex.ToString()); }
            // v2.12: links, backlog picks, the library section on Stats, apps to close and the checklist (in memory only)
            try
            {
                var lines = new List<string>();
                var lg = games.FirstOrDefault(x => x.Id == "steam:1966720") ?? games.FirstOrDefault(x => x.Source == "steam") ?? games[0];
                var ld = S.For(lg.Id); ld.Links.Clear();
                foreach (var t in new[] { new[] { "Wiki", "lethal-company.fandom.com/wiki" }, new[] { "", "https://discord.gg/example" }, new[] { "Bad", "not a link" }, new[] { "Js", "javascript:alert(1)" }, new[] { "Ftp", "ftp://x.com/a" } })
                { var m = GameLinks.Make(t[0], t[1]); lines.Add("make '" + t[0] + "' '" + t[1] + "' -> " + (m ?? "(refused)")); if (m != null) ld.Links.Add(m); }
                foreach (var g in new[] { lg }.Concat(games.Where(x => x.Source != "steam").Take(1)))
                    lines.Add("auto " + g.Name + ": " + string.Join(" ; ", GameLinks.Auto(g).Select(a => a[0] + "=" + a[1])));
                Select(lg); ShowLinks(linksBtn); linksPop.IsOpen = false; var lpc = linksPop.Child; linksPop.Child = null;
                var lw = new Window { Background = (Brush)FindResource("Bg"), Foreground = Foreground, FontFamily = FontFamily, FontSize = FontSize, Content = lpc };
                Snap.Save(lw, System.IO.Path.Combine(dir, "links.png"), 380, 0); lw.Content = null; linksPop.Child = lpc;
                lines.Add("menu: " + string.Join(", ", LinksMenu(lg).Items.OfType<MenuItem>().Select(x => (string)x.Header)));
                // backlog
                foreach (var g in games) { S.For(g.Id).Status = ""; S.For(g.Id).Rating = 0; }
                if (games.Count > 3) { S.For(games[0].Id).Status = "playing"; S.For(games[0].Id).Rating = 4; S.For(games[1].Id).Status = "beaten"; S.For(games[1].Id).Rating = 5; S.For(games[2].Id).Status = "plan"; S.For(games[3].Id).Status = "completed"; S.For(games[3].Id).Rating = 3; }
                lines.Add("backlog: " + string.Join(", ", games.Where(InBacklog).Select(x => x.Name + (S.For(x.Id).Status == "plan" ? " [plan]" : ""))));
                foreach (var mode in new[] { "backlog", "backlog", "backlog", "fav", "old", "any" }) { sel = null; var p = Surprise(mode); lines.Add("pick " + mode + " -> " + (p == null ? "(none) " : p.Name + " ") + "| " + statusText.Text); }
                var backlogPicks = new HashSet<string>(); for (int i = 0; i < 40; i++) { var p = Surprise("backlog"); if (p != null) backlogPicks.Add(p.Id); }
                lines.Add("40 backlog picks all in backlog: " + backlogPicks.All(id => InBacklog(games.First(x => x.Id == id))) + " (" + backlogPicks.Count + " different)");
                var keepGames = games; games = games.Where(x => !InBacklog(x)).ToList(); var none = Surprise("backlog"); lines.Add("empty backlog -> " + (none == null) + " | " + statusText.Text); games = keepGames;
                File.WriteAllText(System.IO.Path.Combine(dir, "picker.txt"), string.Join("\r\n", lines));
                statsMode = "days"; FillStats(); statsPanel.Visibility = Visibility.Visible; statsScroll.MaxHeight = 5000;
                Snap.Save(this, System.IO.Path.Combine(dir, "stats-library.png"), 1180, 1500); statsPanel.Visibility = Visibility.Collapsed;
                // close when it starts + the checklist on the When this game starts card
                var cd = S.For(lg.Id); cd.CloseFirst = new List<string> { @"C:\Program Files\Google\Chrome\Application\chrome.exe", @"C:\Windows\explorer.exe" }; cd.OpenWith = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.System) + @"\notepad.exe" }; cd.DailyLimit = 120;
                S.StartOpen = true; Select(lg); Snap.Save(this, System.IO.Path.Combine(dir, "checklist.png"), 1180, 1000); S.StartOpen = false; ShowStartSection();
                try { var gw2 = new GameSettingsWindow(lg, cd, S, false); Snap.Save(gw2, System.IO.Path.Combine(dir, "gamesettings-212.png"), 480, 0); } catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "gs212.err.txt"), ex.ToString()); }
                File.AppendAllText(System.IO.Path.Combine(dir, "picker.txt"), "\r\nchecklist: " + checklist.Text.Replace("\n", " / ") + "\r\nrunning apps that could be closed: " + string.Join(", ", CloseApps.Running().Select(x => x.Value)) +
                    "\r\nallowed explorer/steam/hallowmixer/chrome/notepad: " + CloseApps.Allowed("explorer.exe") + "/" + CloseApps.Allowed("steam") + "/" + CloseApps.Allowed(@"C:\x\HallowMixer.exe") + "/" + CloseApps.Allowed("chrome.exe") + "/" + CloseApps.Allowed("notepad"));
                ShowChecklist(lg, new GameData()); File.AppendAllText(System.IO.Path.Combine(dir, "picker.txt"), "\r\nchecklist with nothing set visible: " + (checklist.Visibility == Visibility.Visible));
                // the start / stop flow with a stand-in app (test runs only): closed when the game starts, back when it closes
                var standin = Environment.GetEnvironmentVariable("HALLOW_STANDIN");
                if (!string.IsNullOrEmpty(standin) && File.Exists(standin))
                {
                    S.DiscordOnStart = false; S.DiscordRecap = false; S.BreakMinutes = 0;
                    var fg = new Game { Id = "custom:standin-test", Name = "Stand-in game", Source = "custom", InstallDir = @"C:\NoSuchGame", Launch = "" };
                    var fd = S.For(fg.Id); fd.CloseFirst = new List<string> { standin, "explorer.exe" }; fd.Preset = ""; fd.GameSession = false; fd.OpenWith = new List<string>();
                    Func<int> cnt = () => Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(standin)).Length;
                    var before = cnt(); OnStarted(fg); Thread.Sleep(1500); var afterStart = cnt(); var startMsg = statusText.Text;
                    OnStopped(fg, DateTime.Now.AddMinutes(-5)); Thread.Sleep(1500); var afterStop = cnt(); var stopMsg = statusText.Text;
                    fd.NoReopen = true; OnStarted(fg); Thread.Sleep(1500); var c3 = cnt(); OnStopped(fg, DateTime.Now); Thread.Sleep(800); var c4 = cnt();
                    File.AppendAllText(System.IO.Path.Combine(dir, "picker.txt"), "\r\nflow: running before " + before + ", after start " + afterStart + " (" + startMsg + "), after stop " + afterStop + " (" + stopMsg + ")" +
                        "\r\nwith 'don't reopen': after start " + c3 + ", after stop " + c4 + "  explorer still running: " + (Process.GetProcessesByName("explorer").Length > 0));
                    S.Games.Remove(fg.Id);
                }
            }
            catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "v212.err.txt"), ex.ToString()); }
            // v2.14: the power plan (with the in-memory FakePowerCfg - snapshots never touch Windows' plans) and the family tiles
            try
            {
                var fake = PowerPlans.Runner as FakePowerCfg; var lines = new List<string> { "runner: " + PowerPlans.Runner.GetType().Name };
                if (fake != null)
                {
                    var pg = games.FirstOrDefault(x => x.Name.StartsWith("Delta")) ?? games[0]; var pd = S.For(pg.Id);
                    S.PowerPlanAll = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"; pd.PowerPlan = ""; pd.GameSession = false; Plans(true);
                    S.StartOpen = true; Select(pg); Snap.Save(this, System.IO.Path.Combine(dir, "power-checklist.png"), 1180, 1000); S.StartOpen = false; ShowStartSection();
                    lines.Add("checklist: " + checklist.Text.Replace("\n", " / "));
                    try { var gw3 = new GameSettingsWindow(pg, pd, S, false); Snap.Save(gw3, System.IO.Path.Combine(dir, "gamesettings-214.png"), 480, 0); } catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "gs214.err.txt"), ex.ToString()); }
                    var fg = new Game { Id = "custom:power-test", Name = "Power test game", Source = "custom", InstallDir = @"C:\NoSuchGame", Launch = "" };
                    var fd = S.For(fg.Id); fd.Preset = ""; fd.GameSession = false; fd.OpenWith = new List<string>(); fd.CloseFirst = new List<string>();
                    S.DiscordOnStart = false; S.DiscordRecap = false; S.BreakMinutes = 0;
                    fd.PowerPlan = "e9a42b02-d5df-448d-aa00-03f14749eb61";
                    lines.Add("before: " + fake.Active); OnStarted(fg); lines.Add("started: " + fake.Active + " | " + statusText.Text + " | restore=" + S.PowerRestore);
                    OnStopped(fg, DateTime.Now.AddMinutes(-3)); lines.Add("stopped: " + fake.Active + " | " + statusText.Text + " | restore=" + S.PowerRestore);
                    fd.PowerPlan = ""; OnStarted(fg); lines.Add("all-games plan: " + fake.Active); fake.Active = "a1841308-3541-4fab-bc81-f71556f20b4a";
                    OnStopped(fg, DateTime.Now); lines.Add("changed by hand meanwhile -> stays: " + fake.Active + " | " + statusText.Text);
                    fd.PowerPlan = "none"; fake.Active = "381b4222-f694-41f0-9685-ff5bb260df2e"; OnStarted(fg); lines.Add("none: " + fake.Active); OnStopped(fg, DateTime.Now);
                    lines.Add("commands: " + string.Join(" ; ", fake.Log));
                    S.Games.Remove(fg.Id); S.PowerPlanAll = ""; pd.PowerPlan = "";
                }
                File.WriteAllText(System.IO.Path.Combine(dir, "power.txt"), string.Join("\r\n", lines));
                ShowFamily(true); Snap.Save(this, System.IO.Path.Combine(dir, "family-tiles.png"), 1180, 780); Snap.Save(this, System.IO.Path.Combine(dir, "family-tiles-wide.png"), 1500, 780);
            }
            catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "v214.err.txt"), ex.ToString()); }
            // v2.17: the Family page - every Hallow app with its live line and quick actions (and the whole page on a tall window)
            try
            {
                familyBtn.IsChecked = true;
                Snap.Save(this, System.IO.Path.Combine(dir, "family.png"), 1180, 900);
                Snap.Save(this, System.IO.Path.Combine(dir, "family-narrow.png"), 960, 640);   // v2.28
                var keepMax = familyScroll.MaxHeight; familyScroll.MaxHeight = 3000; Snap.Save(this, System.IO.Path.Combine(dir, "family-all.png"), 1180, 1960); familyScroll.MaxHeight = keepMax;   // v2.23: taller - Capture and Display add a row (v2.24: the restore had slipped into the comment; v2.26: 1680 - Uninstall starts another row; v2.27: 1960 - Doctor / Updates and Clock / Fonts add a row each)
                familyBtn.IsChecked = false;
                File.WriteAllText(System.IO.Path.Combine(dir, "family-cards.txt"), FamilyCardsText());
            }
            catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "family.err.txt"), ex.ToString()); }
            // v2.19: Restart family apps after updates (Settings, with one app opted out) and Start the apps I usually run (Family page)
            try
            {
                var inst = FamilyRestarter.Installed(); var keepOpt = S.RestartOptOut; var keepOn = S.RestartAfterUpdates;
                S.RestartAfterUpdates = true; S.RestartOptOut = new List<string>(); if (inst.Count > 1) S.RestartOptOut.Add(inst[1].Key);
                var outer = (Grid)Content; outer.Children.Remove(settingsPanel); settingsPanel = BuildSettings(); outer.Children.Add(settingsPanel);
                settingsPanel.Visibility = Visibility.Visible; Snap.Save(this, System.IO.Path.Combine(dir, "settings-restart.png"), 1180, 1300); settingsPanel.Visibility = Visibility.Collapsed;
                var lines = new List<string> { "installed (watched): " + string.Join(", ", inst.Select(i => i.Key)), "opted out: " + string.Join(", ", S.RestartOptOut) };
                usualForSnap = inst.Select(i => i.Key).Where(a => !FamilyRestarter.NoteOnly.Contains(a) && !FamilyRestarter.NotUsual(a)).Take(3).ToList();
                familyBtn.IsChecked = true; Snap.Save(this, System.IO.Path.Combine(dir, "family-restart.png"), 1180, 900);
                lines.Add("usual button: " + (usualBtn != null ? usualBtn.Visibility + " | " + usualBtn.Content + " | " + usualBtn.ToolTip : "missing").Replace("\n", " / "));
                familyBtn.IsChecked = false; usualForSnap = null;
                File.WriteAllText(System.IO.Path.Combine(dir, "restart.txt"), string.Join("\r\n", lines));
                S.RestartOptOut = keepOpt; S.RestartAfterUpdates = keepOn;
                outer.Children.Remove(settingsPanel); settingsPanel = BuildSettings(); outer.Children.Add(settingsPanel);
            }
            catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "restart.err.txt"), ex.ToString()); }
            // v2.21: the Family checkup (test family only)
            try { SnapCheckup(dir); } catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "checkup.err.txt"), ex.ToString()); }
            try { SnapRealChecks(dir); } catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "realchecks.err.txt"), ex.ToString()); }
            // v2.15: more windows (What's new, Emulators, notes), and light / dark switched live - by the Look setting and by rewriting the TEST theme.json
            try
            {
                var tag = HallowLook.Light ? "-light" : "-dark";
                Snap.Save(new CrashWindow(System.IO.Path.Combine(dir, "2026-11-20 21-14-03.txt")), System.IO.Path.Combine(dir, "crash" + tag + ".png"), 460, 0);
                Snap.Save(new WhatsNewWindow(WhatsNew.Notes.Take(2).ToList()), System.IO.Path.Combine(dir, "whatsnew" + tag + ".png"), 560, 0);
                var emu = S.Emulators.Count > 0 ? S.Emulators : new List<Emulator> { new Emulator { Name = "Sample emulator", Exe = @"C:\Emulators\sample.exe", Folder = @"C:\ROMs\SNES", Types = "sfc smc zip" } };
                Snap.Save(new EmulatorsWindow(emu), System.IO.Path.Combine(dir, "emulators" + tag + ".png"), 640, 0);
                if (df != null) { Select(df); ShowNotes(notesBtn); notesPop.IsOpen = false; var npc = notesPop.Child; notesPop.Child = null;
                    var nw = new Window { Background = (Brush)FindResource("Bg"), Foreground = Foreground, FontFamily = FontFamily, FontSize = FontSize, Content = npc };
                    Snap.Save(nw, System.IO.Path.Combine(dir, "notes" + tag + ".png"), 380, 0); nw.Content = null; notesPop.Child = npc; }
                if (df != null) Select(df);
                var lines = new List<string> { "start: light=" + HallowLook.Light + " family=" + HallowLook.FamilyMode + " look=" + HallowLook.LookOverride + " accent=" + HallowLook.Accent + " text accent=" + HallowLook.AccentText };
                var keepLook = HallowLook.LookOverride;
                HallowLook.LookOverride = HallowLook.Light ? "dark" : "light"; ApplyAccent(false);
                lines.Add("Look setting -> " + HallowLook.LookOverride + ": light=" + HallowLook.Light + " window bg=" + Background);
                Snap.Save(this, System.IO.Path.Combine(dir, "look-switched" + tag + ".png"), 1180, 780);
                gearBtn.IsChecked = true; Snap.Save(this, System.IO.Path.Combine(dir, "look-switched-settings" + tag + ".png"), 1180, 900); gearBtn.IsChecked = false;
                HallowLook.LookOverride = keepLook; ApplyAccent(false); lines.Add("back: light=" + HallowLook.Light);
                var fam = Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST");
                var tf = string.IsNullOrEmpty(fam) ? null : System.IO.Path.Combine(HallowLook.Dir, "theme.json");
                if (tf != null && File.Exists(tf) && HallowLook.NormalLook(keepLook) == "hallow")
                {
                    var keep = File.ReadAllText(tf); var flip = HallowLook.Light ? "dark" : "light";
                    File.WriteAllText(tf, System.Text.RegularExpressions.Regex.Replace(keep, "\"mode\"\\s*:\\s*\"[^\"]*\"", "\"mode\": \"" + flip + "\""));
                    ApplyAccent(false); lines.Add("test theme.json mode -> " + flip + ": light=" + HallowLook.Light);
                    Snap.Save(this, System.IO.Path.Combine(dir, "file-switched" + tag + ".png"), 1180, 780);
                    File.WriteAllText(tf, keep); ApplyAccent(false); lines.Add("test theme.json restored: light=" + HallowLook.Light);
                }
                File.AppendAllText(System.IO.Path.Combine(dir, "mode.txt"), string.Join("\r\n", lines) + "\r\n");
            }
            catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "mode.err.txt"), ex.ToString()); }
            // v2.5: the accent switched live (the window rebuilds with the new theme)
            try { var was = HallowLook.UseShared; HallowLook.UseShared = !was; ApplyAccent(false); Snap.Save(this, System.IO.Path.Combine(dir, "accent-switched.png"), 1180, 780); HallowLook.UseShared = was; ApplyAccent(false); } catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "accent.err.txt"), ex.ToString()); }
        }
    }
}
