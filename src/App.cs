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
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace HallowHub
{
    public static class Program
    {
        public const string Version = "1.0";
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
        Grid heroGrid; Image heroImg, heroLogo; TextBlock heroName, heroStats; Button playBtn; ToggleButton favBtn;
        ComboBox presetBox, sortBox; CheckBox sessionBox; TextBlock sessionNote, countText, statusText, searchHint;
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

            tracker.Started = OnStarted; tracker.Stopped = OnStopped;
            tracker.Counted = (g, m) => { S.For(g.Id).AddMinutes(m, DateTime.Now); S.Save(); RefreshCard(g); if (sel == g) ShowHero(); UpdateStatus(); };
            Rescan();
            if (!Snap.On)
            {
                SetupTray();
                poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                poll.Tick += (s, e) => { try { tracker.Poll(games); } catch { } if (starting != null && (DateTime.Now - startingAt).TotalSeconds > 90) { starting = null; ShowHero(); } };
                poll.Start();
                slow = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                slow.Tick += (s, e) => { CheckLinks(); if (sel != null && tracker.Now.ContainsKey(sel.Id)) ShowHero(); };
                slow.Start();
                Dispatcher.BeginInvoke(new Action(() => { try { tracker.Poll(games); } catch { } CheckLinks(); }), DispatcherPriority.ApplicationIdle);
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
            gearBtn.Checked += (s, e) => { settingsPanel.Visibility = Visibility.Visible; CheckLinks(); };
            gearBtn.Unchecked += (s, e) => settingsPanel.Visibility = Visibility.Collapsed;
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
            var hero = new Border { Height = 300, CornerRadius = new CornerRadius(18), Background = (Brush)FindResource("Panel"), Margin = new Thickness(0, 0, 0, 16) };
            heroGrid = new Grid();
            hero.SizeChanged += (s, e) => heroGrid.Clip = new RectangleGeometry(new Rect(0, 0, hero.ActualWidth, hero.ActualHeight), 18, 18);
            heroImg = new Image { Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Center };
            heroGrid.Children.Add(heroImg);
            var fadeL = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            fadeL.GradientStops.Add(new GradientStop(Color.FromArgb(240, 0x12, 0x0F, 0x18), 0)); fadeL.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0x12, 0x0F, 0x18), 0.5)); fadeL.GradientStops.Add(new GradientStop(Color.FromArgb(40, 0x12, 0x0F, 0x18), 1));
            heroGrid.Children.Add(new Rectangle { Fill = fadeL });
            var fadeB = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fadeB.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x12, 0x0F, 0x18), 0.45)); fadeB.GradientStops.Add(new GradientStop(Color.FromArgb(220, 0x12, 0x0F, 0x18), 1));
            heroGrid.Children.Add(new Rectangle { Fill = fadeB });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(34, 0, 0, 28), MaxWidth = 520 };
            heroLogo = new Image { MaxHeight = 104, MaxWidth = 400, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 4) };
            heroName = new TextBlock { FontSize = 34, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
            heroStats = new TextBlock { FontSize = 13, Foreground = B("#D6D0E2"), Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
            info.Children.Add(heroLogo); info.Children.Add(heroName); info.Children.Add(heroStats);
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
            info.Children.Add(actions);
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
            gridScroll = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(gridScroll, 3); root.Children.Add(gridScroll);

            statusText = new TextBlock { Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, Margin = new Thickness(2, 10, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(statusText, 4); root.Children.Add(statusText);

            var outer = new Grid(); outer.Children.Add(root);
            settingsPanel = BuildSettings(); outer.Children.Add(settingsPanel);
            return outer;
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
            sp.Children.Add(new TextBlock { Text = "Hallow Hub " + Program.Version + "  -  icons: Font Awesome Free (CC BY 4.0)", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 12, 0, 0), FontSize = 11 });
            b.Child = sp; return b;
        }

        // ================================================================ data -> screen
        void Rescan()
        {
            games = Library.Scan(S.Custom);
            // games without art (Battle.net, Epic, added) borrow Steam's artwork when the same game is on Steam
            foreach (var g in games.Where(x => x.Cover == null))
            {
                var twin = games.FirstOrDefault(x => x.Source == "steam" && x.Cover != null && string.Equals(x.Name, g.Name, StringComparison.OrdinalIgnoreCase));
                if (twin != null) { g.Cover = twin.Cover; g.Hero = twin.Hero; g.Logo = twin.Logo; g.Header = twin.Header; }
            }
            if (sel != null) sel = games.FirstOrDefault(x => x.Id == sel.Id);
            if (sel == null) sel = games.FirstOrDefault(x => x.Id == S.Selected);
            if (sel == null) sel = Sorted(games.Where(x => !S.For(x.Id).Hidden)).FirstOrDefault();
            RebuildChips(); RebuildGrid(); ShowHero(); UpdateStatus();
        }

        IEnumerable<Game> Sorted(IEnumerable<Game> list)
        {
            if (S.Sort == "played") return list.OrderByDescending(g => S.For(g.Id).Minutes).ThenBy(g => g.Name);
            if (S.Sort == "name") return list.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase);
            return list.OrderByDescending(g => S.For(g.Id).Last ?? DateTime.MinValue).ThenByDescending(g => S.For(g.Id).Minutes).ThenBy(g => g.Name);
        }

        void RebuildChips()
        {
            chips.Children.Clear();
            var opts = new List<Tuple<string, string>> { Tuple.Create("all", "All"), Tuple.Create("fav", "Favorites") };
            foreach (var src in new[] { "steam", "battlenet", "epic", "custom" })
                if (games.Any(g => g.Source == src)) opts.Add(Tuple.Create(src, games.First(g => g.Source == src).SourceName));
            if (!opts.Any(o => o.Item1 == S.Filter)) S.Filter = "all";
            foreach (var o in opts)
            {
                var key = o.Item1;
                var c = new ToggleButton { Content = o.Item2, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 8, 0), IsChecked = S.Filter == key };
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
                (S.Filter == "all" || (S.Filter == "fav" ? S.For(g.Id).Favorite : g.Source == S.Filter)) &&
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
            foot.Children.Add(new TextBlock { Text = d.Minutes < 1 ? "New" : HubSettings.Duration(d.Minutes), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = B("#ECE8F4") });
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
            if (tracker.Now.ContainsKey(g.Id))
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
        void Paint(Border card, bool hot)
        {
            var g = card.Tag as Game;
            card.BorderBrush = g == sel ? B("#F07820") : hot ? B("#55496A") : Brushes.Transparent;
            card.Effect = g == sel ? new System.Windows.Media.Effects.DropShadowEffect { Color = C("#F07820"), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.45 } : null;
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
            item(S.For(g.Id).Favorite ? "Remove from favorites" : "Add to favorites", "star", () => { var d = S.For(g.Id); d.Favorite = !d.Favorite; S.Save(); RefreshCard(g); if (g == sel) ShowHero(); });
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
                    playBtn.Visibility = Visibility.Collapsed; favBtn.Visibility = Visibility.Collapsed; return;
                }
                playBtn.Visibility = Visibility.Visible; favBtn.Visibility = Visibility.Visible;
                var d = S.For(g.Id);
                heroImg.Source = Img(g.Hero, 1600) ?? Img(g.Header, 0) ?? Img(g.Cover, 0);
                heroImg.Effect = g.Hero == null && heroImg.Source != null ? new System.Windows.Media.Effects.BlurEffect { Radius = 30 } : null;
                heroLogo.Source = Img(g.Logo, 0);
                heroLogo.Visibility = heroLogo.Source != null ? Visibility.Visible : Visibility.Collapsed;
                heroName.Text = g.Name; heroName.Visibility = heroLogo.Source != null ? Visibility.Collapsed : Visibility.Visible;
                var week = d.MinutesSince(DateTime.Today.AddDays(-6));
                heroStats.Text = (d.Minutes < 1 ? "Not played with Hallow Hub yet" : HubSettings.Duration(d.Minutes) + " played") +
                    (week >= 1 ? "   ·   " + HubSettings.Duration(week) + " this week" : "") +
                    "   ·   Last played: " + HubSettings.Ago(d.Last) + "   ·   " + g.SourceName;
                favBtn.IsChecked = d.Favorite;
                Tracker.Running r;
                if (tracker.Now.TryGetValue(g.Id, out r))
                {
                    var mins = (DateTime.Now - r.Since).TotalMinutes;
                    playBtn.Content = "Playing  ·  " + (mins < 1 ? "just started" : HubSettings.Duration(mins));
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
            var run = tracker.Now.Values.Select(r => r.Game.Name).ToList();
            statusText.Text = (run.Count > 0 ? "Playing " + string.Join(", ", run) + "   ·   " : "") +
                "Today: " + (today < 1 ? "0 min" : HubSettings.Duration(today)) + "   ·   This week: " + (week < 1 ? "0 min" : HubSettings.Duration(week)) +
                "   ·   " + games.Count + " games installed";
        }
        string statusNote; DateTime statusUntil;
        void Status(string msg) { statusNote = msg; statusUntil = DateTime.Now.AddSeconds(8); statusText.Text = msg; }

        // ================================================================ actions
        void Play(Game g)
        {
            if (g == null) return;
            if (tracker.Now.ContainsKey(g.Id)) { Status(g.Name + " is already running."); return; }
            try
            {
                var psi = new ProcessStartInfo(g.Launch, g.Args ?? "") { UseShellExecute = true };
                if (File.Exists(g.Launch)) psi.WorkingDirectory = System.IO.Path.GetDirectoryName(g.Launch);
                Process.Start(psi);
                starting = g.Id; startingAt = DateTime.Now; ShowHero();
                Status("Starting " + g.Name + "...");
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
            foreach (var g in games.Where(x => !S.For(x.Id).Hidden).OrderByDescending(x => S.For(x.Id).Last ?? DateTime.MinValue).ThenByDescending(x => S.For(x.Id).Minutes).Take(8))
            {
                var gg = g; var mi = new WF.ToolStripMenuItem(g.Name + (tracker.Now.ContainsKey(g.Id) ? "   (playing)" : ""));
                mi.Click += (s, e) => Dispatcher.BeginInvoke(new Action(() => Play(gg))); trayGames.DropDownItems.Add(mi);
            }
            trayGames.Enabled = trayGames.DropDownItems.Count > 0;
            var run = tracker.Now.Values.Select(r => r.Game.Name).FirstOrDefault();
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
            Snap.Save(this, System.IO.Path.Combine(dir, "main.png"), 1180, 780);
            settingsPanel.Visibility = Visibility.Visible;
            Snap.Save(this, System.IO.Path.Combine(dir, "settings.png"), 1180, 780);
        }
    }
}
