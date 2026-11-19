// Hallow Hub - all your games in one place. Play starts the game; while it runs, Hub can switch your Hallow Mixer preset
// and turn on a Hallow Toolkit game session, and it counts your playtime. WPF built in code (C# 5 / .NET Framework 4.8).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace HallowHub
{
    public static class Program
    {
        public const string Version = "1.2";
        [STAThread]
        public static void Main(string[] args)
        {
            int si = Array.IndexOf(args, "--snap");
            if (si >= 0 && si + 1 < args.Length)
            {
                Snap.On = true; HubSettings.Frozen = true;
                var sa = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(sa);
                new HubWindow(true).SnapAll(args[si + 1]); return;
            }
            bool created;
            var mutex = new Mutex(true, @"Local\HallowHub", out created);
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HallowHubShow");
            if (!created) { showEvent.Set(); return; }
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(app);
            var w = new HubWindow(args.Contains("--tray"));
            var t = new Thread(() => { while (showEvent.WaitOne()) w.Dispatcher.BeginInvoke(new Action(w.ShowFromTray)); }) { IsBackground = true };
            t.Start();
            app.Run();
            GC.KeepAlive(mutex);
        }
        static void LoadTheme(Application app)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("HallowHub.Theme.xaml"))
                app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(s));
        }
    }

    public class HubWindow : Window
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
        static SolidColorBrush B(string hex) { return new SolidColorBrush(C(hex)); }
        static readonly Dictionary<string, string> SourceColor = new Dictionary<string, string> {
            { "steam", "#5AB4F0" }, { "battlenet", "#4AA8FF" }, { "epic", "#ECE8F4" }, { "custom", "#A98BE0" } };

        readonly HubSettings S = HubSettings.Load();
        List<Game> games = new List<Game>();
        readonly Tracker tracker = new Tracker();
        readonly bool startHidden;
        Game sel; bool exiting, toldTray; string sessionFor, starting; DateTime startingAt;
        List<string> presets;   // null = mixer not running

        // controls
        Border heroBorder; WrapPanel heroPills; StackPanel playOn; GradientStop heroTint;
        Grid heroGrid; Image heroImg, heroLogo; TextBlock heroName, heroStats; Button playBtn; ToggleButton favBtn;
        ComboBox presetBox, sortBox; CheckBox sessionBox; TextBlock sessionNote, countText, statusText, searchHint;
        StackPanel contRow; FrameworkElement contLabel, libLabel;
        WrapPanel grid; StackPanel chips; TextBox search; Border settingsPanel; ToggleButton gearBtn; ScrollViewer gridScroll;
        CheckBox startupBox; TextBlock linkInfo;
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
            SourceInitialized += (s, e) => { int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, 4); };
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

            tracker.Started = g => OnStarted(Main(g)); tracker.Stopped = g => OnStopped(Main(g));
            tracker.Counted = (g0, m) => { var g = Main(g0); S.For(g.Id).AddMinutes(m, DateTime.Now); S.Save(); RefreshCard(g); if (sel == g) ShowHero(); UpdateStatus(); };
            Rescan();
            if (!Snap.On)
            {
                SetupTray();
                poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                poll.Tick += (s, e) => { try { tracker.Poll(AllCopies().ToList()); } catch { } if (starting != null && (DateTime.Now - startingAt).TotalSeconds > 90) { starting = null; ShowHero(); } };
                poll.Start();
                PreviewKeyDown += OnKey;
                var padTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) }; padTimer.Tick += (s, e) => { try { TickPad(); } catch { } }; padTimer.Start();
                slow = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                slow.Tick += (s, e) => { CheckLinks(); if (sel != null && IsRunning(sel)) ShowHero(); };
                slow.Start();
                Dispatcher.BeginInvoke(new Action(() => { try { tracker.Poll(AllCopies().ToList()); } catch { } CheckLinks(); }), DispatcherPriority.ApplicationIdle);
                if (!startHidden) Show();
            }
            else CheckLinks();
        }

        static ImageSource AppIcon()
        {
            var ico = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            return Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        // ================================================================ layout
        UIElement Build()
        {
            var root = new Grid { Margin = new Thickness(22, 14, 22, 12) };
            foreach (var h in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
                root.RowDefinitions.Add(new RowDefinition { Height = h });

            // ---- header: logo, search, buttons
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            try { title.Children.Add(new Image { Source = AppIcon(), Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 0) }); } catch { }
            title.Children.Add(new TextBlock { Text = "HALLOW", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = B("#F07820"), VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock { Text = " HUB", FontSize = 22, FontWeight = FontWeights.Light, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(title, Dock.Left); head.Children.Add(title);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var add = new Button { Content = "Add game", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Add any game or app by its .exe" };
            add.Click += (s, e) => AddGame(); right.Children.Add(add);
            var refresh = new Button { Content = "Refresh", Tag = "icon", Width = 36, Height = 34, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Look for newly installed games" };
            refresh.Click += (s, e) => { Rescan(); Status("Library refreshed - " + games.Count + " games."); }; right.Children.Add(refresh);
            gearBtn = new ToggleButton { Content = "Settings", Tag = "icon", Width = 36, Height = 34, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), ToolTip = "Settings" };
            gearBtn.Checked += (s, e) => { if (statsBtn != null) statsBtn.IsChecked = false; settingsPanel.Visibility = Visibility.Visible; CheckLinks(); };
            gearBtn.Unchecked += (s, e) => settingsPanel.Visibility = Visibility.Collapsed;
            statsBtn = new ToggleButton { Content = "Stats", Tag = "icon", Width = 36, Height = 34, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Your playtime" };
            statsBtn.Checked += (s, e) => { gearBtn.IsChecked = false; FillStats(); statsPanel.Visibility = Visibility.Visible; };
            statsBtn.Unchecked += (s, e) => statsPanel.Visibility = Visibility.Collapsed;
            var dice = new Button { Content = "Surprise me", Tag = "icon", Width = 36, Height = 34, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), ToolTip = "What should I play? (Ctrl+R, or X on a controller)" };
            dice.Click += (s, e) => Surprise();
            right.Children.Add(dice);
            right.Children.Add(statsBtn);
            right.Children.Add(gearBtn);
            DockPanel.SetDock(right, Dock.Right); head.Children.Add(right);
            var sg = new Grid { Width = 360, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            search = new TextBox { Style = (Style)FindResource("Search") };
            search.TextChanged += (s, e) => { searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RebuildGrid(); };
            var si = Icons.Make("magnifying-glass", 14, (Brush)FindResource("Muted")); si.HorizontalAlignment = HorizontalAlignment.Left; si.Margin = new Thickness(13, 0, 0, 0); si.IsHitTestVisible = false;
            searchHint = new TextBlock { Text = "Search your games", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(36, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, FontSize = 13 };
            sg.Children.Add(search); sg.Children.Add(si); sg.Children.Add(searchHint);
            head.Children.Add(sg);
            Grid.SetRow(head, 0); root.Children.Add(head);

            // ---- hero: the selected game
            var hero = heroBorder = new Border { Height = 330, CornerRadius = new CornerRadius(18), Background = (Brush)FindResource("Panel"), Margin = new Thickness(0, 0, 0, 16), BorderThickness = new Thickness(1.5) };
            heroGrid = new Grid();
            hero.SizeChanged += (s, e) => heroGrid.Clip = new RectangleGeometry(new Rect(0, 0, hero.ActualWidth, hero.ActualHeight), 18, 18);
            heroImg = new Image { Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Center };
            heroGrid.Children.Add(heroImg);
            var fadeL = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            heroTint = new GradientStop(Color.FromArgb(240, 0x12, 0x0F, 0x18), 0); fadeL.GradientStops.Add(heroTint); fadeL.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0x12, 0x0F, 0x18), 0.5)); fadeL.GradientStops.Add(new GradientStop(Color.FromArgb(40, 0x12, 0x0F, 0x18), 1));
            heroGrid.Children.Add(new Rectangle { Fill = fadeL });
            var fadeB = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fadeB.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x12, 0x0F, 0x18), 0.45)); fadeB.GradientStops.Add(new GradientStop(Color.FromArgb(220, 0x12, 0x0F, 0x18), 1));
            heroGrid.Children.Add(new Rectangle { Fill = fadeB });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(34, 0, 0, 28), MaxWidth = 520 };
            heroLogo = new Image { MaxHeight = 104, MaxWidth = 400, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 4) };
            heroName = new TextBlock { FontSize = 34, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
            heroStats = new TextBlock { FontSize = 13, Foreground = B("#D6D0E2"), Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
            heroPills = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            info.Children.Add(heroLogo); info.Children.Add(heroName); info.Children.Add(heroStats); info.Children.Add(heroPills);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
            playBtn = new Button { Content = "Play", Style = (Style)FindResource("Accent"), FontSize = 15, Padding = new Thickness(30, 10, 30, 10), MinWidth = 150 };
            playBtn.Click += (s, e) => Play(sel);
            actions.Children.Add(playBtn);
            favBtn = new ToggleButton { Content = "Favorite", Tag = "icon", Width = 42, Height = 42, Padding = new Thickness(0), Style = (Style)FindResource("Chip"), Margin = new Thickness(10, 0, 0, 0), ToolTip = "Favorite" };
            favBtn.Click += (s, e) => { if (sel == null) return; S.For(sel.Id).Favorite = favBtn.IsChecked == true; S.Save(); RefreshCard(sel); if (S.Filter == "fav") RebuildGrid(); };
            actions.Children.Add(favBtn);
            var folder = new Button { Content = "Open folder", Tag = "icon", Width = 42, Height = 42, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Open the game's folder" };
            folder.Click += (s, e) => OpenFolder(sel);
            actions.Children.Add(folder);
            var gset = new Button { Content = "Game settings", Tag = "icon", Width = 42, Height = 42, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Launch options, run as admin, collections" };
            gset.Click += (s, e) => GameSettings(sel);
            actions.Children.Add(gset);
            info.Children.Add(actions);
            playOn = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            info.Children.Add(playOn);
            heroGrid.Children.Add(info);
            // "when it starts" card
            var opts = new Border { Background = new SolidColorBrush(Color.FromArgb(205, 0x1A, 0x15, 0x22)), CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 14, 18, 16), Width = 310,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 26, 26),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), BorderThickness = new Thickness(1) };
            var op = new StackPanel();
            var oh = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var bolt = Icons.Make("bolt", 12, B("#F07820")); bolt.Margin = new Thickness(0, 0, 8, 0); oh.Children.Add(bolt);
            oh.Children.Add(new TextBlock { Text = "WHEN THIS GAME STARTS", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = B("#A98BE0") });
            op.Children.Add(oh);
            op.Children.Add(new TextBlock { Text = "Hallow Mixer preset", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, Margin = new Thickness(0, 0, 0, 4) });
            presetBox = new ComboBox { Style = (Style)FindResource("Combo"), Margin = new Thickness(0, 0, 0, 12) };
            presetBox.SelectionChanged += (s, e) =>
            {
                if (loading || sel == null) return; var it = presetBox.SelectedItem as ComboBoxItem; if (it == null) return;
                S.For(sel.Id).Preset = (it.Tag as string) ?? ""; S.Save();
                Status(S.For(sel.Id).Preset == "" ? sel.Name + " leaves your mixer as it is." : sel.Name + " switches Hallow Mixer to \"" + S.For(sel.Id).Preset + "\" when it starts.");
            };
            op.Children.Add(presetBox);
            sessionBox = new CheckBox { Content = new TextBlock { Text = "Hallow Toolkit game session", TextWrapping = TextWrapping.Wrap }, Style = (Style)FindResource("Switch"), Margin = new Thickness(0) };
            sessionBox.Click += (s, e) =>
            {
                if (sel == null) return; S.For(sel.Id).GameSession = sessionBox.IsChecked == true; S.Save();
                Status(sessionBox.IsChecked == true ? "Game session on for " + sel.Name + ": Boost apps close and High performance power while it runs, back after." : "No game session for " + sel.Name + ".");
            };
            op.Children.Add(sessionBox);
            sessionNote = new TextBlock { Foreground = (Brush)FindResource("Muted"), FontSize = 11, Margin = new Thickness(52, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
            op.Children.Add(sessionNote);
            opts.Child = op;
            heroGrid.Children.Add(opts);
            hero.Child = heroGrid;
            Grid.SetRow(hero, 1); root.Children.Add(hero);

            // ---- filters + sort
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            sortBox = new ComboBox { Style = (Style)FindResource("Combo"), Width = 170, VerticalAlignment = VerticalAlignment.Center };
            foreach (var o in new[] { Tuple.Create("recent", "Recently played"), Tuple.Create("played", "Most played"), Tuple.Create("name", "A to Z") })
                sortBox.Items.Add(new ComboBoxItem { Content = o.Item2, Tag = o.Item1 });
            sortBox.SelectionChanged += (s, e) => { if (loading) return; var it = sortBox.SelectedItem as ComboBoxItem; if (it != null) { S.Sort = (string)it.Tag; S.Save(); RebuildGrid(); } };
            DockPanel.SetDock(sortBox, Dock.Right); bar.Children.Add(sortBox);
            countText = new TextBlock { Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            DockPanel.SetDock(countText, Dock.Right); bar.Children.Add(countText);
            chips = new StackPanel { Orientation = Orientation.Horizontal };
            bar.Children.Add(chips);
            Grid.SetRow(bar, 2); root.Children.Add(bar);

            // ---- the library
            grid = new WrapPanel { Margin = new Thickness(-7, 0, -7, 0) };
            var lib = new StackPanel();
            contLabel = SectionLabel("CONTINUE PLAYING", "clock");
            contRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-7, 0, 0, 6) };
            libLabel = SectionLabel("LIBRARY", "table-cells-large");
            lib.Children.Add(contLabel); lib.Children.Add(contRow); lib.Children.Add(libLabel); lib.Children.Add(grid);
            gridScroll = new ScrollViewer { Content = lib, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(gridScroll, 3); root.Children.Add(gridScroll);

            statusText = new TextBlock { Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, Margin = new Thickness(2, 10, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(statusText, 4); root.Children.Add(statusText);

            var outer = new Grid(); outer.Children.Add(root);
            settingsPanel = BuildSettings(); outer.Children.Add(settingsPanel);
            statsPanel = BuildStats(); outer.Children.Add(statsPanel);
            return outer;
        }

        // ---- v1.1 stats: totals, a 14-day chart and your top games
        Border statsPanel; ToggleButton statsBtn; StackPanel statsBody;
        Border BuildStats()
        {
            var b = new Border { Background = B("#211B2A"), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24, 16, 24, 22), Width = 800, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 62, 0, 0), Visibility = Visibility.Collapsed };
            b.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, Opacity = 0.6, ShadowDepth = 4 };
            var sp = new StackPanel();
            var sh = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var close = new Button { Content = "x", Tag = "icon", Style = (Style)FindResource("Btn"), Width = 30, Height = 28, Padding = new Thickness(0), ToolTip = "Close" };
            close.Click += (s, e) => statsBtn.IsChecked = false;
            DockPanel.SetDock(close, Dock.Right); sh.Children.Add(close);
            var ci = Icons.Make("chart-line", 16, B("#F07820")); ci.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(ci, Dock.Left); sh.Children.Add(ci);
            sh.Children.Add(new TextBlock { Text = "Your playtime", FontWeight = FontWeights.Bold, FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(sh);
            statsBody = new StackPanel(); sp.Children.Add(statsBody);
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
            tile("Today", today, "#F07820", null); tile("This week", week, "#A98BE0", null);
            tile("Last 2 weeks", two, "#6ED28C", "Steam + Hallow Hub"); tile("All time", all, "#5AB4F0", vis.Count(g => Total(g) >= 1) + " games played");
            statsBody.Children.Add(tiles);
            // 14-day chart (days are counted by Hallow Hub while it runs)
            statsBody.Children.Add(SectionLabel("LAST 14 DAYS", "calendar-days"));
            var days = Enumerable.Range(0, 14).Select(i => DateTime.Today.AddDays(i - 13)).ToList();
            var per = days.Select(d => S.Games.Values.Sum(x => x.MinutesOn(d))).ToList();
            double max = Math.Max(per.Max(), 1);
            var chart = new Grid { Height = 150, Margin = new Thickness(0, 0, 0, 18) };
            for (int i = 0; i < 14; i++) chart.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 14; i++)
            {
                var col = new DockPanel { Margin = new Thickness(4, 0, 4, 0) };
                var lbl = new TextBlock { Text = days[i].ToString("ddd").Substring(0, 2) + " " + days[i].Day, FontSize = 10, Foreground = days[i] == DateTime.Today ? B("#F07820") : (Brush)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
                DockPanel.SetDock(lbl, Dock.Bottom); col.Children.Add(lbl);
                var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                fill.GradientStops.Add(new GradientStop(C("#FF9A45"), 0)); fill.GradientStops.Add(new GradientStop(C("#F05A8C"), 1));
                var h = per[i] < 1 ? 3 : Math.Max(6, per[i] / max * 112);
                col.Children.Add(new Border { Height = h, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(5), Background = per[i] < 1 ? B("#2E2639") : (Brush)fill,
                    ToolTip = days[i].ToString("dddd d MMM") + ": " + (per[i] < 1 ? "nothing" : HubSettings.Duration(per[i])) });
                Grid.SetColumn(col, i); chart.Children.Add(col);
            }
            if (per.Max() < 1)
                { var note = new TextBlock { Text = "This fills in day by day as you play with Hallow Hub running.", Foreground = (Brush)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 30) }; Grid.SetColumnSpan(note, 14); chart.Children.Add(note); }
            statsBody.Children.Add(chart);
            // top games
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
            var a1 = top("MOST PLAYED", "trophy", Total); var a2 = top("LAST 2 WEEKS", "fire", Recent);
            Grid.SetColumn(a2, 2); two2.Children.Add(a1); two2.Children.Add(a2);
            statsBody.Children.Add(two2);
        }

        Border BuildSettings()
        {
            var b = new Border { Background = B("#241E2E"), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18, 14, 18, 16), Width = 420, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 62, 22, 0), Visibility = Visibility.Collapsed };
            b.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, Opacity = 0.5, ShadowDepth = 4 };
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
            linkInfo = new TextBlock { Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), FontSize = 11.5, LineHeight = 18 };
            sp.Children.Add(linkInfo);
            sp.Children.Add(new TextBlock { Text = "Playtime counts for any game running from its install folder, however you start it (Steam, Battle.net, a shortcut).",
                Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontSize = 11.5 });
            sp.Children.Add(new TextBlock { Text = "Keys: arrows move, Enter plays, Ctrl+F search, Ctrl+R surprise me, F5 refresh, Esc back.\nController: d-pad or stick, A play, X surprise me, Y favorite, LB/RB filters, B back, Start stats.",
                Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontSize = 11.5, LineHeight = 18 });
            sp.Children.Add(new TextBlock { Text = "Hallow Hub " + Program.Version + "  -  icons: Font Awesome Free (CC BY 4.0)", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 12, 0, 0), FontSize = 11 });
            b.Child = sp; return b;
        }

        // ================================================================ v1.1 helpers
        // total playtime: Steam's own count or Hub's, whichever is bigger (Steam also counts the time Hub sees)
        double Total(Game g) { return Math.Max(S.For(g.Id).Minutes, g.SteamMinutes); }
        DateTime? LastOf(Game g)
        {
            var a = S.For(g.Id).Last; var b = g.SteamLast;
            if (a != null) a = a.Value.ToUniversalTime();
            if (a == null) return b; if (b == null) return a; return a > b ? a : b;
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
            var want = S.For(g.Id).PlayOn;
            return string.IsNullOrEmpty(want) ? g : (g.Id == want ? g : g.Alts.FirstOrDefault(a => a.Id == want) ?? g);
        }
        // started straight from its .exe (so launch options and run-as-admin apply)
        static bool ExeLaunch(Game c) { return c != null && File.Exists(c.Launch) && string.IsNullOrEmpty(c.Args) && c.Source != "battlenet"; }
        static string Size(long bytes) { return bytes <= 0 ? null : bytes >= (1L << 30) ? (bytes / (double)(1L << 30)).ToString("0") + " GB" : (bytes / (double)(1L << 20)).ToString("0") + " MB"; }

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
        void Rescan()
        {
            games = Library.Scan(S.Custom);
            // games without art (Battle.net, Epic, added) borrow Steam's artwork when the same game is on Steam
            foreach (var g in games.Where(x => x.Cover == null))
            {
                var twin = AllCopies().FirstOrDefault(x => x.Source == "steam" && x.Cover != null && Library.Norm(x.Name) == Library.Norm(g.Name));
                if (twin != null) { g.Cover = twin.Cover; g.Hero = twin.Hero; g.Logo = twin.Logo; g.Header = twin.Header; }
            }
            if (sel != null) sel = games.FirstOrDefault(x => x.Id == sel.Id);
            if (sel == null) sel = games.FirstOrDefault(x => x.Id == S.Selected);
            if (sel == null) sel = Sorted(games.Where(x => !S.For(x.Id).Hidden)).FirstOrDefault();
            RebuildChips(); RebuildGrid(); ShowHero(); UpdateStatus();
        }

        IEnumerable<Game> Sorted(IEnumerable<Game> list)
        {
            if (S.Sort == "played") return list.OrderByDescending(g => Total(g)).ThenBy(g => g.Name);
            if (S.Sort == "name") return list.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase);
            return list.OrderByDescending(g => LastOf(g) ?? DateTime.MinValue).ThenByDescending(g => Total(g)).ThenBy(g => g.Name);
        }

        void RebuildChips()
        {
            chips.Children.Clear();
            var opts = new List<Tuple<string, string>> { Tuple.Create("all", "All"), Tuple.Create("fav", "Favorites") };
            foreach (var src in new[] { "steam", "battlenet", "epic", "custom" })
                if (games.Any(g => g.Source == src)) opts.Add(Tuple.Create(src, games.First(g => g.Source == src).SourceName));
            foreach (var c in S.Collections) if (games.Any(g => S.For(g.Id).Tags.Contains(c))) opts.Add(Tuple.Create("col:" + c, c));
            if (!opts.Any(o => o.Item1 == S.Filter)) S.Filter = "all";
            foreach (var o in opts)
            {
                var key = o.Item1;
                object content = o.Item2;
                if (key.StartsWith("col:"))
                {
                    var cp = new StackPanel { Orientation = Orientation.Horizontal };
                    var ti = new System.Windows.Shapes.Path { Data = Icons.Get("tag"), Stretch = Stretch.Uniform, Width = 12, Height = 12, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
                    cp.Children.Add(ti); cp.Children.Add(new TextBlock { Text = o.Item2, VerticalAlignment = VerticalAlignment.Center }); content = cp;
                }
                var c = new ToggleButton { Content = content, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 8, 0), IsChecked = S.Filter == key };
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
                (S.ShowHidden || !S.For(g.Id).Hidden) &&
                (S.Filter == "all" || (S.Filter == "fav" ? S.For(g.Id).Favorite : S.Filter.StartsWith("col:") ? S.For(g.Id).Tags.Contains(S.Filter.Substring(4)) : g.Source == S.Filter)) &&
                (q.Length == 0 || g.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0))).ToList();
        }

        void RebuildGrid()
        {
            if (grid == null) return;
            grid.Children.Clear(); cards.Clear();
            var list = Visible();
            foreach (var g in list) { var c = Card(g); cards[g.Id] = c; grid.Children.Add(c); }
            if (list.Count == 0)
                grid.Children.Add(new TextBlock { Text = games.Count == 0 ? "No games found yet. Click Add game to add one by its .exe." : "No games match.", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(10, 20, 0, 0), FontSize = 14 });
            countText.Text = list.Count == 1 ? "1 game" : list.Count + " games";
            // continue playing: the 4 most recent, on the main view only
            contRow.Children.Clear();
            var recent = (S.Filter == "all" && search.Text.Trim().Length == 0) ? games.Where(g => !S.For(g.Id).Hidden && LastOf(g) != null).OrderByDescending(g => LastOf(g)).Take(4).ToList() : new List<Game>();
            foreach (var g in recent) contRow.Children.Add(Wide(g));
            var show = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            contLabel.Visibility = show; contRow.Visibility = show; libLabel.Visibility = show;
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
            var card = new Border { Width = 160, Height = 240, Margin = new Thickness(7), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent, Background = (Brush)FindResource("Panel"), Cursor = Cursors.Hand, Tag = g,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1), ToolTip = g.Name };
            var inner = new Grid { Clip = new RectangleGeometry(new Rect(0, 0, 156, 236), 12, 12) };
            var cover = Img(g.Cover, 320);
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
                st.Children.Add(new TextBlock { Text = g.Name, FontSize = 15, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = Brushes.White });
                inner.Children.Add(st);
            }
            var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0)); shade.GradientStops.Add(new GradientStop(Color.FromArgb(215, 0x10, 0x0C, 0x16), 1));
            inner.Children.Add(new Rectangle { Fill = shade, Height = 64, VerticalAlignment = VerticalAlignment.Bottom });
            var foot = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(11, 0, 0, 10) };
            var clock = Icons.Make("clock", 11, B("#D6D0E2")); clock.Margin = new Thickness(0, 0, 6, 0); foot.Children.Add(clock);
            foot.Children.Add(new TextBlock { Text = Total(g) < 1 ? "New" : HubSettings.Duration(Total(g)), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = B("#ECE8F4") });
            inner.Children.Add(foot);
            var srcIcon = g.Source == "steam" ? "steam" : g.Source == "battlenet" ? "battle-net" : g.Source == "custom" ? "windows" : "gamepad";
            var badge = Icons.Make(srcIcon, 12, new SolidColorBrush(Color.FromArgb(200, 0xEC, 0xE8, 0xF4)));
            badge.HorizontalAlignment = HorizontalAlignment.Right; badge.VerticalAlignment = VerticalAlignment.Bottom; badge.Margin = new Thickness(0, 0, 11, 11);
            inner.Children.Add(badge);
            if (d.Favorite)
            {
                var star = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = new SolidColorBrush(Color.FromArgb(200, 0x16, 0x12, 0x1C)),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0), Child = Icons.Make("star", 12, B("#F0BE5A")) };
                inner.Children.Add(star);
            }
            if (!IsRunning(g) && (g.NeedsUpdate || g.Updating))
            {
                var up = new Border { CornerRadius = new CornerRadius(9), Background = B(g.Updating ? "#5AB4F0" : "#F0BE5A"), Padding = new Thickness(8, 2, 8, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 8, 0, 0),
                    Child = new TextBlock { Text = g.Updating ? "UPDATING" : "UPDATE", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = B("#241A04") }, ToolTip = g.Updating ? "Steam is updating it" : "Steam has an update waiting - it downloads when you press Play" };
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
            card.MouseEnter += (s, e) => { Zoom(card, 1.04); Paint(card, true); };
            card.MouseLeave += (s, e) => { Zoom(card, 1.0); Paint(card, false); };
            card.MouseLeftButtonUp += (s, e) => Select(g);
            card.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { Select(g); Play(g); } };
            card.ContextMenu = Menu(g);
            return card;
        }
        FrameworkElement SectionLabel(string text, string icon)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, 4, 0, 8) };
            var ic = Icons.Make(icon, 11, B("#A98BE0")); ic.Margin = new Thickness(0, 0, 8, 0); sp.Children.Add(ic);
            sp.Children.Add(new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = B("#A98BE0") });
            return sp;
        }
        // a wide "continue playing" card: the game's header art, when you last played and for how long
        Border Wide(Game g)
        {
            var w = new Border { Width = 262, Height = 150, Margin = new Thickness(7, 0, 7, 0), CornerRadius = new CornerRadius(14), Background = (Brush)FindResource("Panel"), Cursor = Cursors.Hand,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1), BorderThickness = new Thickness(1), BorderBrush = B("#2E2639"), ToolTip = g.Name };
            var gi = new Grid { Clip = new RectangleGeometry(new Rect(0, 0, 260, 148), 13, 13) };
            gi.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); gi.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var art = Img(g.Header, 520) ?? Img(g.Hero, 520) ?? Img(g.Cover, 320);
            if (art != null) gi.Children.Add(new Image { Source = art, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Center });
            else gi.Children.Add(new TextBlock { Text = g.Name, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap });
            var ac = Accent(g);
            var foot = new DockPanel { Background = new SolidColorBrush(Color.FromArgb(255, (byte)(0x1C + (ac.R - 0x1C) * 0.12), (byte)(0x17 + (ac.G - 0x17) * 0.12), (byte)(0x24 + (ac.B - 0x24) * 0.12))) };
            var running = IsRunning(g);
            var left = new TextBlock { Text = running ? "Playing now" : HubSettings.Ago(LastOf(g)), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = running ? B("#6ED28C") : B("#ECE8F4"), Margin = new Thickness(11, 7, 0, 8) };
            var right = new TextBlock { Text = HubSettings.Duration(Total(g)), FontSize = 11.5, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 7, 11, 8) };
            DockPanel.SetDock(right, Dock.Right); foot.Children.Add(right); foot.Children.Add(left);
            Grid.SetRow(foot, 1); gi.Children.Add(foot);
            var play = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = B("#F07820"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 10, 10), Child = Icons.Make("play", 13, B("#1A0E04")), Opacity = 0, ToolTip = "Play " + g.Name };
            ((FrameworkElement)play.Child).Margin = new Thickness(3, 0, 0, 0);
            play.MouseLeftButtonUp += (s, e) => { e.Handled = true; Select(g); Play(g); };
            gi.Children.Add(play);
            w.Child = gi;
            w.MouseEnter += (s, e) => { Zoom(w, 1.03); w.BorderBrush = new SolidColorBrush(ac); play.Opacity = 1; };
            w.MouseLeave += (s, e) => { Zoom(w, 1.0); w.BorderBrush = B("#2E2639"); play.Opacity = 0; };
            w.MouseLeftButtonUp += (s, e) => Select(g);
            w.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { Select(g); Play(g); } };
            w.ContextMenu = Menu(g);
            return w;
        }
        void Paint(Border card, bool hot)
        {
            var g = card.Tag as Game;
            var ac = g == null ? C("#F07820") : Accent(g);
            card.BorderBrush = g == sel ? new SolidColorBrush(ac) : hot ? B("#55496A") : Brushes.Transparent;
            card.Effect = g == sel ? new System.Windows.Media.Effects.DropShadowEffect { Color = ac, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.6 } : null;
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
            item("Game settings and collections...", "sliders", () => { Select(g); GameSettings(g); });
            item("Open folder", "folder-open", () => OpenFolder(g));
            if (g.Source == "steam") item("Steam store page", "arrow-up-right-from-square", () => OpenUrl("steam://store/" + g.Id.Substring(6)));
            item(S.For(g.Id).Hidden ? "Unhide" : "Hide from the library", S.For(g.Id).Hidden ? "eye" : "eye-slash", () => { var d = S.For(g.Id); d.Hidden = !d.Hidden; S.Save(); RebuildGrid(); Status(d.Hidden ? g.Name + " is hidden. Settings > Show hidden games brings it back." : g.Name + " is back in the library."); });
            if (g.Source == "custom") item("Remove from Hallow Hub", "trash-can", () => { S.Custom.RemoveAll(x => x.Id == g.Id); S.Save(); Rescan(); Status(g.Name + " removed (its playtime is kept)."); });
            return m;
        }
        void RefreshCard(Game g)
        {
            Border old; if (g == null || !cards.TryGetValue(g.Id, out old)) return;
            int i = grid.Children.IndexOf(old); if (i < 0) return;
            var c = Card(g); cards[g.Id] = c; grid.Children.RemoveAt(i); grid.Children.Insert(i, c);
        }

        void Select(Game g)
        {
            if (g == null) return;
            var prev = sel; sel = g; S.Selected = g.Id; S.Save();
            Border b; if (prev != null && cards.TryGetValue(prev.Id, out b)) Paint(b, false);
            if (cards.TryGetValue(g.Id, out b)) Paint(b, b.IsMouseOver);
            ShowHero();
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
                    heroImg.Source = null; heroLogo.Source = null; heroName.Text = "Your games show up here"; heroStats.Text = "Hallow Hub finds Steam, Battle.net and Epic Games games by itself. Add anything else with Add game.";
                    playBtn.Visibility = Visibility.Collapsed; favBtn.Visibility = Visibility.Collapsed; heroPills.Children.Clear(); playOn.Children.Clear(); return;
                }
                playBtn.Visibility = Visibility.Visible; favBtn.Visibility = Visibility.Visible;
                var d = S.For(g.Id);
                heroImg.Source = Img(g.Hero, 1600) ?? Img(g.Header, 0) ?? Img(g.Cover, 0);
                heroImg.Effect = g.Hero == null && heroImg.Source != null ? new System.Windows.Media.Effects.BlurEffect { Radius = 30 } : null;
                heroLogo.Source = Img(g.Logo, 0);
                heroLogo.Visibility = heroLogo.Source != null ? Visibility.Visible : Visibility.Collapsed;
                heroName.Text = g.Name; heroName.Visibility = heroLogo.Source != null ? Visibility.Collapsed : Visibility.Visible;
                var total = Total(g); var recent = Recent(g);
                heroStats.Text = (total < 1 ? "Not played yet" : HubSettings.Duration(total) + " played") +
                    (recent >= 1 ? "   \u00B7   " + HubSettings.Duration(recent) + " in the last 2 weeks" : "") +
                    "   \u00B7   Last played: " + HubSettings.Ago(LastOf(g));
                heroPills.Children.Clear();
                Action<string, string, string> pill = (icon, text, col) =>
                {
                    var sp2 = new StackPanel { Orientation = Orientation.Horizontal };
                    if (icon != null) { var ic = Icons.Make(icon, 11, B(col)); ic.Margin = new Thickness(0, 0, 6, 0); sp2.Children.Add(ic); }
                    sp2.Children.Add(new TextBlock { Text = text, FontSize = 11.5, Foreground = B(col), FontWeight = FontWeights.SemiBold });
                    heroPills.Children.Add(new Border { Child = sp2, CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 10, 4), Margin = new Thickness(0, 0, 6, 6),
                        Background = new SolidColorBrush(Color.FromArgb(150, 0x1A, 0x15, 0x22)), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1) });
                };
                var srcIcon = g.Source == "steam" ? "steam" : g.Source == "battlenet" ? "battle-net" : g.Source == "custom" ? "windows" : "gamepad";
                pill(srcIcon, g.SourceName + (g.Alts.Count > 0 ? " + " + string.Join(" + ", g.Alts.Select(a => a.SourceName)) : ""), "#ECE8F4");
                if (g.Updating) pill("arrows-rotate", "Updating", "#8FCBF5");
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
                        var chip = new ToggleButton { Content = copy.SourceName, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 6, 0), IsChecked = copy == cur, Padding = new Thickness(10, 4, 10, 4),
                            ToolTip = (copy.Drive != null ? copy.Drive + "  " : "") + copy.InstallDir + (copy.UpdatedAt > DateTime.MinValue ? "\nUpdated " + copy.UpdatedAt.ToString("d MMM yyyy") : "") };
                        chip.Click += (o, e2) => { S.For(g.Id).PlayOn = cp.Id; S.Save(); ShowHero(); Status("Play starts " + g.Name + " on " + cp.SourceName + "."); };
                        playOn.Children.Add(chip);
                    }
                }
                var ac = Accent(g);
                heroBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(150, ac.R, ac.G, ac.B));
                heroBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = ac, BlurRadius = 34, ShadowDepth = 0, Opacity = 0.35 };
                heroTint.Color = Color.FromArgb(240, (byte)(0x12 + (ac.R - 0x12) * 0.14), (byte)(0x0F + (ac.G - 0x0F) * 0.14), (byte)(0x18 + (ac.B - 0x18) * 0.14));
                favBtn.IsChecked = d.Favorite;
                var r = RunningOf(g);
                if (r != null)
                {
                    var mins = (DateTime.Now - r.Since).TotalMinutes;
                    playBtn.Content = "Playing  \u00B7  " + (mins < 1 ? "just started" : HubSettings.Duration(mins));
                    playBtn.Background = B("#6ED28C"); playBtn.BorderBrush = B("#8EE0A6"); playBtn.ToolTip = "It's running - have fun";
                }
                else
                {
                    playBtn.ClearValue(BackgroundProperty); playBtn.ClearValue(Border.BorderBrushProperty); playBtn.ClearValue(BorderBrushProperty);
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
            }
            finally { loading = false; }
        }

        void CheckLinks()
        {
            var was = presets == null ? "" : string.Join("|", presets);
            presets = Mixer.Presets();
            var now = presets == null ? "" : string.Join("|", presets);
            if (was != now || (presets == null) != (was == "")) ShowHero();
            if (linkInfo != null)
                linkInfo.Text = "Hallow Mixer: " + (presets == null ? "not running (start it to pick presets per game)" : "connected - " + presets.Count + " presets") + "\n" +
                    "Hallow Toolkit: " + (Toolkit.Script == null ? "not installed" : Toolkit.CanSession ? "found - game sessions available" : "found - update to 5.2 for game sessions");
            UpdateStatus();
        }

        void UpdateStatus()
        {
            if (statusText == null || statusNote != null && DateTime.Now < statusUntil) return;
            var week = S.Games.Values.Sum(x => x.MinutesSince(DateTime.Today.AddDays(-6)));
            var today = S.Games.Values.Sum(x => x.MinutesSince(DateTime.Today));
            var run = tracker.Now.Values.Select(r => Main(r.Game).Name).Distinct().ToList();
            statusText.Text = (run.Count > 0 ? "Playing " + string.Join(", ", run) + "   \u00B7   " : "") +
                "Today: " + (today < 1 ? "0 min" : HubSettings.Duration(today)) + "   \u00B7   This week: " + (week < 1 ? "0 min" : HubSettings.Duration(week)) +
                "   \u00B7   " + games.Count + " games installed";
        }
        string statusNote; DateTime statusUntil;
        void Status(string msg) { statusNote = msg; statusUntil = DateTime.Now.AddSeconds(8); statusText.Text = msg; }

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
                try { Process.Start(psi); }
                catch (System.ComponentModel.Win32Exception wx) { if (wx.NativeErrorCode == 1223) { Status("Cancelled - " + g.Name + " wasn't started."); return; } throw; }
                starting = g.Id; startingAt = DateTime.Now; ShowHero();
                Status("Starting " + g.Name + (g.Alts.Count > 0 ? " on " + copy.SourceName : "") + "...");
            }
            catch (Exception ex) { Status("Couldn't start " + g.Name + ": " + ex.Message); }
        }

        void OnStarted(Game g)
        {
            var d = S.For(g.Id); d.Sessions++; d.LastPlayed = DateTime.Now.ToString("o"); S.Save();
            if (starting == g.Id) starting = null;
            var did = new List<string>();
            if (d.Preset != "" && Mixer.UsePreset(d.Preset)) did.Add("Hallow Mixer on \"" + d.Preset + "\"");
            if (d.GameSession && Toolkit.CanSession && sessionFor == null) { Toolkit.Session(true); sessionFor = g.Id; did.Add("game session on"); }
            Status(g.Name + " started" + (did.Count > 0 ? " - " + string.Join(", ", did) : "") + ".");
            RefreshCard(g); if (g == sel) ShowHero(); UpdateTrayMenu();
        }
        void OnStopped(Game g)
        {
            var d = S.For(g.Id); d.LastPlayed = DateTime.Now.ToString("o"); S.Save();
            bool ended = false;
            if (sessionFor == g.Id) { Toolkit.Session(false); sessionFor = null; ended = true; }
            Status(g.Name + " closed" + (ended ? " - game session ended, everything is back" : "") + ". " + HubSettings.Duration(d.Minutes) + " played in total.");
            RefreshCard(g); if (g == sel) ShowHero(); UpdateTrayMenu();
        }

        void GameSettings(Game g)
        {
            if (g == null) return;
            var w = new GameSettingsWindow(g, S.For(g.Id), S, ExeLaunch(PlayCopy(g))) { Owner = this };
            if (w.ShowDialog() == true) { S.Save(); RebuildChips(); RebuildGrid(); ShowHero(); Status(g.Name + " settings saved."); }
        }

        // "What should I play?" - favourites and games you haven't touched for a while come up more often
        readonly Random rng = new Random();
        void Surprise()
        {
            var pool = games.Where(g => !S.For(g.Id).Hidden && !IsRunning(g) && g != sel).ToList();
            if (pool.Count == 0) { Status("Nothing to pick from yet."); return; }
            var weights = pool.Select(g =>
            {
                var last = LastOf(g); double days = last == null ? 30 : (DateTime.UtcNow - last.Value).TotalDays;
                return 1 + (S.For(g.Id).Favorite ? 2 : 0) + Math.Min(days / 7, 3) + (Total(g) >= 60 ? 1 : 0);
            }).ToList();
            double r = rng.NextDouble() * weights.Sum(); int i = 0;
            for (; i < pool.Count - 1; i++) { r -= weights[i]; if (r <= 0) break; }
            var pick = pool[i];
            if (!Visible().Contains(pick)) { S.Filter = "all"; search.Text = ""; RebuildChips(); RebuildGrid(); }
            Select(pick); Reveal(pick);
            var lastP = LastOf(pick);
            Status("How about " + pick.Name + "? " + (lastP == null ? "You haven't played it yet." : "Last played " + HubSettings.Ago(lastP).ToLowerInvariant() + ".") + "  Press Enter (or A) to play.");
        }
        void Reveal(Game g) { Border b; if (g != null && cards.TryGetValue(g.Id, out b)) b.BringIntoView(); }

        // ---- keyboard and controller: arrows / d-pad move, Enter / A play, Esc / B back
        void Move(int dx, int dy)
        {
            var list = Visible(); if (list.Count == 0) return;
            int i = sel == null ? -1 : list.IndexOf(sel);
            if (i < 0) { Select(list[0]); Reveal(list[0]); return; }
            int cols = Math.Max(1, (int)((grid.ActualWidth + 1) / 174));
            int j = Math.Max(0, Math.Min(list.Count - 1, i + dx + dy * cols));
            if (j != i) { Select(list[j]); Reveal(list[j]); }
        }
        bool Back()
        {
            if (statsBtn.IsChecked == true) { statsBtn.IsChecked = false; return true; }
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
            if (ctrl && e.Key == Key.R) { Surprise(); e.Handled = true; return; }
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
        WF.ToolStripMenuItem trayGames;
        void SetupTray()
        {
            tray = new WF.NotifyIcon { Text = "Hallow Hub", Visible = true };
            try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { tray.Icon = System.Drawing.SystemIcons.Application; }
            var menu = new WF.ContextMenuStrip();
            var open = new WF.ToolStripMenuItem("Open Hallow Hub"); open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
            open.Click += (s, e) => ShowFromTray(); menu.Items.Add(open);
            trayGames = new WF.ToolStripMenuItem("Play"); menu.Items.Add(trayGames);
            menu.Items.Add(new WF.ToolStripSeparator());
            var quit = new WF.ToolStripMenuItem("Quit"); quit.Click += (s, e) => Quit(); menu.Items.Add(quit);
            menu.Opening += (s, e) => UpdateTrayMenu();
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) ShowFromTray(); };
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
            trayGames.Enabled = trayGames.DropDownItems.Count > 0;
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
            exiting = true; S.Save();
            if (sessionFor != null) { Toolkit.Session(false); sessionFor = null; }
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
            settingsPanel.Visibility = Visibility.Visible;
            Snap.Save(this, System.IO.Path.Combine(dir, "settings.png"), 1180, 780);
            settingsPanel.Visibility = Visibility.Collapsed;
            FillStats(); statsPanel.Visibility = Visibility.Visible;
            Snap.Save(this, System.IO.Path.Combine(dir, "stats.png"), 1180, 780);
            statsPanel.Visibility = Visibility.Collapsed;
            try { var gw = new GameSettingsWindow(df ?? games[0], S.For((df ?? games[0]).Id), S, false); Snap.Save(gw, System.IO.Path.Combine(dir, "gamesettings.png"), 480, 0); } catch (Exception ex) { File.WriteAllText(System.IO.Path.Combine(dir, "gs.err.txt"), ex.ToString()); }
            Surprise(); Snap.Save(this, System.IO.Path.Combine(dir, "surprise.png"), 1180, 780);
            var ow = games.FirstOrDefault(g => g.Alts.Count > 0); if (ow != null) { Select(ow); Snap.Save(this, System.IO.Path.Combine(dir, "merged.png"), 1180, 780); }
        }
    }
}
