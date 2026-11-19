// Hallow Hub v2.0 - Big Picture: a full-screen, controller-first view for the couch, and a quick launcher from the tray icon.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HallowHub
{
    public partial class HubWindow
    {
        Window big;
        public void OpenBigPicture()
        {
            if (big != null) { big.Activate(); return; }
            var list = Visible(); if (list.Count == 0) list = games.ToList();
            int index = Math.Max(0, sel == null ? 0 : list.IndexOf(sel));
            const double W = 230, H = 345, GAP = 26;

            big = new Window { Title = "Hallow Hub - Big Picture", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = B("#0E0B12"),
                Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), ShowInTaskbar = true, Icon = Icon };
            var root = new Grid { ClipToBounds = true };
            var bg = new Image { Stretch = Stretch.UniformToFill, Opacity = 0.55, Effect = new BlurEffect { Radius = 6 } };
            root.Children.Add(bg);
            var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0x0E, 0x0B, 0x12), 0)); shade.GradientStops.Add(new GradientStop(Color.FromArgb(235, 0x0E, 0x0B, 0x12), 0.62));
            root.Children.Add(new Rectangle { Fill = shade });

            var layout = new Grid { Margin = new Thickness(70, 40, 70, 40) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(H + 70) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            // top bar: logo, filter, clock
            var top = new DockPanel();
            var brand = new StackPanel { Orientation = Orientation.Horizontal };
            try { brand.Children.Add(new Image { Source = Icon, Width = 34, Height = 34, Margin = new Thickness(0, 0, 12, 0) }); } catch { }
            brand.Children.Add(new TextBlock { Text = "HALLOW", FontSize = 26, FontWeight = FontWeights.Bold, Foreground = B("#F07820"), VerticalAlignment = VerticalAlignment.Center });
            brand.Children.Add(new TextBlock { Text = " HUB", FontSize = 26, FontWeight = FontWeights.Light, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(brand, Dock.Left); top.Children.Add(brand);
            var clock = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(clock, Dock.Right); top.Children.Add(clock);
            var filterText = new TextBlock { FontSize = 15, Foreground = B("#CFC8DC"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            top.Children.Add(filterText);
            Grid.SetRow(top, 0); layout.Children.Add(top);
            // the selected game
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 26) };
            var logo = new Image { MaxHeight = 150, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform };
            var name = new TextBlock { FontSize = 54, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
            var stats = new TextBlock { FontSize = 18, Foreground = B("#D6D0E2"), Margin = new Thickness(0, 14, 0, 0) };
            var hint = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
            info.Children.Add(logo); info.Children.Add(name); info.Children.Add(stats); info.Children.Add(hint);
            Grid.SetRow(info, 1); layout.Children.Add(info);
            // the cover row
            var strip = new Canvas { ClipToBounds = false };
            var row = new StackPanel { Orientation = Orientation.Horizontal, RenderTransform = new TranslateTransform() };
            strip.Children.Add(row);
            Grid.SetRow(strip, 2); layout.Children.Add(strip);
            var help = new TextBlock { FontSize = 14, Foreground = B("#8F88A0"), HorizontalAlignment = HorizontalAlignment.Center,
                Text = "A / Enter  play     X  surprise me     Y  favorite     LB / RB  filter     B / Esc  back to the desktop view" };
            Grid.SetRow(help, 3); layout.Children.Add(help);
            root.Children.Add(layout);
            var close = new Button { Content = "x", Tag = "icon", Width = 40, Height = 36, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 14, 0), ToolTip = "Close Big Picture (Esc)" };
            close.Click += (s, e) => big.Close();
            root.Children.Add(close);
            big.Content = root;

            var tiles = new List<Border>();
            Action layoutTiles = null; Action<int, bool> go = null;
            Func<Game, Border> tile = g =>
            {
                var b = new Border { Width = W, Height = H, Margin = new Thickness(0, 0, GAP, 0), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent,
                    Background = B("#211B2A"), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.9, 0.9), Opacity = 0.65, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
                var gi = new Grid { Clip = new RectangleGeometry(new Rect(0, 0, W - 6, H - 6), 13, 13) };
                var art = Img(g.Cover, 460);
                if (art != null) gi.Children.Add(new Image { Source = art, Stretch = Stretch.UniformToFill });
                else gi.Children.Add(new TextBlock { Text = g.Name, FontSize = 22, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center });
                if (IsRunning(g)) gi.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = B("#6ED28C"), Padding = new Thickness(10, 3, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10),
                    Child = new TextBlock { Text = "PLAYING", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = B("#0E2416") } });
                b.Child = gi;
                var gg = g;
                b.MouseLeftButtonUp += (s, e) => { int i = list.IndexOf(gg); if (i >= 0) go(i, true); };
                b.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) Play(gg); };
                return b;
            };
            Action fill = () =>
            {
                row.Children.Clear(); tiles.Clear();
                foreach (var g in list) { var t = tile(g); tiles.Add(t); row.Children.Add(t); }
                filterText.Text = (S.Filter == "all" ? "All games" : S.Filter == "fav" ? "Favorites" : S.Filter == "upd" ? "Needs update" : S.Filter == "stale" ? "Not played lately" : S.Filter.StartsWith("col:") ? S.Filter.Substring(4) : list.Count > 0 ? list[0].SourceName : S.Filter) + "   ·   " + list.Count + " games";
            };
            go = (i, animate) =>
            {
                if (list.Count == 0) { name.Text = "No games here"; logo.Source = null; stats.Text = ""; bg.Source = null; return; }
                index = Math.Max(0, Math.Min(list.Count - 1, i));
                var g = list[index]; Select(g);
                for (int k = 0; k < tiles.Count; k++)
                {
                    var on = k == index; var t = tiles[k]; var st = (ScaleTransform)t.RenderTransform;
                    var to = on ? 1.1 : 0.9; var dur = TimeSpan.FromMilliseconds(animate ? 180 : 0);
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, dur)); st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, dur));
                    t.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(on ? 1 : 0.6, dur));
                    var ac = Accent(list[k]);
                    t.BorderBrush = on ? new SolidColorBrush(ac) : Brushes.Transparent;
                    t.Effect = on ? new DropShadowEffect { Color = ac, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.75 } : null;
                }
                var vw = big.ActualWidth > 0 ? big.ActualWidth : big.Width;
                var x = vw / 2 - 70 - (index * (W + GAP) + W / 2);
                ((TranslateTransform)row.RenderTransform).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(animate ? 260 : 0)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                bg.Source = Img(g.Hero, 1920) ?? Img(g.Header, 0) ?? Img(g.Cover, 0);
                logo.Source = Img(g.Logo, 0); logo.Visibility = logo.Source != null ? Visibility.Visible : Visibility.Collapsed;
                name.Text = g.Name; name.Visibility = logo.Source != null ? Visibility.Collapsed : Visibility.Visible;
                var tot = Total(g);
                stats.Text = (IsRunning(g) ? "Playing now   ·   " : "") + (tot < 1 ? "Not played yet" : HubSettings.Duration(tot) + " played") + "   ·   Last played: " + HubSettings.Ago(LastOf(g)) + "   ·   " + g.SourceName + (S.For(g.Id).Favorite ? "   ·   Favorite" : "");
                hint.Children.Clear();
                var play = new Border { CornerRadius = new CornerRadius(12), Background = B("#F07820"), Padding = new Thickness(26, 12, 30, 12), Cursor = Cursors.Hand };
                var ps = new StackPanel { Orientation = Orientation.Horizontal };
                ps.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = B("#6ED28C"), Margin = new Thickness(0, 0, 12, 0), Child = new TextBlock { Text = "A", FontWeight = FontWeights.Bold, FontSize = 16, Foreground = B("#0E2416"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
                ps.Children.Add(new TextBlock { Text = IsRunning(g) ? "Playing" : "Play", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = B("#1A0E04"), VerticalAlignment = VerticalAlignment.Center });
                play.Child = ps; play.MouseLeftButtonUp += (s, e) => Play(g);
                hint.Children.Add(play);
            };
            Action<int> filter = step =>
            {
                var keys = new List<string> { "all", "fav" };
                foreach (var src in games.Select(x => x.Source).Distinct()) keys.Add(src);
                foreach (var c in S.Collections) keys.Add("col:" + c);
                int i = keys.IndexOf(S.Filter); i = ((i < 0 ? 0 : i) + step + keys.Count) % keys.Count;
                S.Filter = keys[i]; S.Save(); RebuildChips(); RebuildGrid();
                list = Visible(); fill(); go(0, false);
            };
            Action surprise = () => { Surprise(); int i = list.IndexOf(sel); if (i < 0) { S.Filter = "all"; list = Visible(); fill(); i = list.IndexOf(sel); } go(i, true); };
            Action fav = () => { if (list.Count == 0) return; var g = list[index]; var d = S.For(g.Id); d.Favorite = !d.Favorite; S.Save(); RefreshCard(g); go(index, false); };

            big.PreviewKeyDown += (s, e) =>
            {
                switch (e.Key)
                {
                    case Key.Left: go(index - 1, true); e.Handled = true; break;
                    case Key.Right: go(index + 1, true); e.Handled = true; break;
                    case Key.Enter: if (list.Count > 0) Play(list[index]); e.Handled = true; break;
                    case Key.Escape: case Key.F11: big.Close(); e.Handled = true; break;
                }
            };
            var pad2 = new Pad();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            timer.Tick += (s, e) =>
            {
                clock.Text = DateTime.Now.ToString("HH:mm");
                if (!big.IsActive) return;
                ushort b; try { b = pad2.Poll(); } catch { return; }
                if (b == 0) return;
                if ((b & Pad.Left) != 0) go(index - 1, true);
                if ((b & Pad.Right) != 0) go(index + 1, true);
                if ((b & Pad.A) != 0 && list.Count > 0) Play(list[index]);
                if ((b & Pad.B) != 0 || (b & Pad.Back) != 0) big.Close();
                if ((b & Pad.X) != 0) surprise();
                if ((b & Pad.Y) != 0) fav();
                if ((b & Pad.LB) != 0) filter(-1);
                if ((b & Pad.RB) != 0) filter(1);
            };
            big.Loaded += (s, e) => { fill(); go(index, false); timer.Start(); };
            big.SizeChanged += (s, e) => go(index, false);
            big.Closed += (s, e) => { timer.Stop(); big = null; ShowFromTray(); };
            big.SourceInitialized += (s, e) => { int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(big).Handle, 20, ref dark, 4); };
            big.WindowState = WindowState.Maximized;
            if (Snap.On) { big.Width = 1600; big.Height = 900; fill(); go(index, false); return; }
            Hide(); big.Show(); big.Activate();
        }

        // ---- quick launcher: click the tray icon for your recent games, right next to the clock
        Window quick;
        void ShowQuick()
        {
            if (quick != null) { quick.Close(); return; }
            var list = games.Where(x => !S.For(x.Id).Hidden).OrderByDescending(x => LastOf(x) ?? DateTime.MinValue).ThenByDescending(x => Total(x)).Take(6).ToList();
            quick = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true, ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight, FontFamily = new FontFamily("Segoe UI"), Foreground = B("#ECE8F4") };
            var sp = new StackPanel { Width = 340 };
            var head = new DockPanel { Margin = new Thickness(2, 0, 0, 10) };
            var open = new Button { Content = "Open Hallow Hub", Style = (Style)FindResource("Btn"), Padding = new Thickness(10, 4, 10, 4), FontSize = 11.5 };
            open.Click += (s, e) => { quick.Close(); ShowFromTray(); };
            DockPanel.SetDock(open, Dock.Right); head.Children.Add(open);
            head.Children.Add(new TextBlock { Text = "Play", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = B("#F07820"), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(head);
            var grid = new UniformGrid { Columns = 2 };
            foreach (var g in list)
            {
                var gg = g;
                var t = new Border { Height = 82, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, BorderThickness = new Thickness(1.5), BorderBrush = B("#2E2639"), ToolTip = "Play " + g.Name };
                var art = Img(g.Header, 340) ?? Img(g.Cover, 200);
                var gi = new Grid { Clip = new RectangleGeometry(new Rect(0, 0, 159, 79), 9, 9) };
                if (art != null) gi.Children.Add(new Image { Source = art, Stretch = Stretch.UniformToFill });
                else gi.Children.Add(new Rectangle { Fill = B("#2A2333") });
                var sh = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                sh.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.4)); sh.GradientStops.Add(new GradientStop(Color.FromArgb(220, 0x10, 0x0C, 0x16), 1));
                gi.Children.Add(new Rectangle { Fill = sh });
                gi.Children.Add(new TextBlock { Text = IsRunning(g) ? "Playing" : g.Name, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = IsRunning(g) ? B("#6ED28C") : Brushes.White, Margin = new Thickness(8, 0, 8, 6), VerticalAlignment = VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis });
                t.Child = gi;
                var ac = Accent(g);
                t.MouseEnter += (s, e) => t.BorderBrush = new SolidColorBrush(ac);
                t.MouseLeave += (s, e) => t.BorderBrush = B("#2E2639");
                t.MouseLeftButtonUp += (s, e) => { quick.Close(); Select(gg); Play(gg); };
                grid.Children.Add(t);
            }
            sp.Children.Add(grid);
            if (list.Count == 0) sp.Children.Add(new TextBlock { Text = "No games yet.", Foreground = B("#8F88A0") });
            var shell = new Border { Background = B("#1C1724"), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 12, 6, 6), Child = sp, Margin = new Thickness(12),
                Effect = new DropShadowEffect { BlurRadius = 24, Opacity = 0.6, ShadowDepth = 3 } };
            quick.Content = shell;
            quick.Deactivated += (s, e) => { if (quick != null) quick.Close(); };
            quick.Closed += (s, e) => quick = null;
            quick.Loaded += (s, e) => { var wa = SystemParameters.WorkArea; quick.Left = wa.Right - quick.ActualWidth; quick.Top = wa.Bottom - quick.ActualHeight; };
            if (Snap.On) return;
            quick.Show(); quick.Activate();
        }
    }
}
