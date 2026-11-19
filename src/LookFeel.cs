// Hallow Hub v1.7 - look and feel: cover size, list view, slowly moving banner, notes per game, pins.
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace HallowHub
{
    public class Minus : IValueConverter
    {
        public double By;
        public object Convert(object v, Type t, object p, CultureInfo c) { return Math.Max(0, (double)v - By); }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
    }

    public partial class HubWindow
    {
        double K { get { return S.CardScale <= 0 ? 1 : S.CardScale; } }
        Border CardFor(Game g) { return S.View == "list" ? ListRow(g) : Card(g); }

        // ---- list view: one row per game
        Border ListRow(Game g)
        {
            var d = S.For(g.Id);
            var row = new Border { Height = 64, Margin = new Thickness(7, 0, 7, 8), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent,
                Background = (Brush)FindResource("Panel"), Cursor = Cursors.Hand, Tag = g, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
            row.SetBinding(FrameworkElement.WidthProperty, new Binding("ViewportWidth") { Source = gridScroll, Converter = new Minus { By = 16 } });
            var dp = new DockPanel { Margin = new Thickness(6, 0, 14, 0) };
            var art = Img(g.Header, 260) ?? Img(g.Cover, 160);
            var thumb = new Border { Width = 116, Height = 50, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = art != null ? (Brush)new ImageBrush(art) { Stretch = Stretch.UniformToFill } : B("#2E2639") };
            DockPanel.SetDock(thumb, Dock.Left); dp.Children.Add(thumb);
            Func<string, double, TextBlock> col = (text, w) => new TextBlock { Text = text, Width = w, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis };
            var size = col(Size(g.SizeBytes + g.Alts.Sum(a => a.SizeBytes)) ?? "", 80); DockPanel.SetDock(size, Dock.Right); dp.Children.Add(size);
            var last = col(IsRunning(g) ? "Playing now" : HubSettings.Ago(LastOf(g)), 120); if (IsRunning(g)) last.Foreground = B("#6ED28C"); DockPanel.SetDock(last, Dock.Right); dp.Children.Add(last);
            var time = col(Total(g) < 1 ? "New" : HubSettings.Duration(Total(g)), 90); time.Foreground = B("#ECE8F4"); time.FontWeight = FontWeights.SemiBold; DockPanel.SetDock(time, Dock.Right); dp.Children.Add(time);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            if (d.Pinned) { var pi = Icons.Make("thumbtack", 11, B("#F07820")); pi.Margin = new Thickness(0, 0, 7, 0); top.Children.Add(pi); }
            if (d.Favorite) { var fi = Icons.Make("star", 11, B("#F0BE5A")); fi.Margin = new Thickness(0, 0, 7, 0); top.Children.Add(fi); }
            top.Children.Add(new TextBlock { Text = g.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            names.Children.Add(top);
            var sub = g.SourceName + (g.Alts.Count > 0 ? " + " + string.Join(" + ", g.Alts.Select(a => a.SourceName)) : "") +
                (g.Updating ? "   ·   updating" + (g.Progress >= 0 ? " " + (int)(g.Progress * 100) + "%" : "") : g.NeedsUpdate ? "   ·   update waiting" : "") +
                (d.Tags.Count > 0 ? "   ·   " + string.Join(", ", d.Tags) : "");
            names.Children.Add(new TextBlock { Text = sub, Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
            dp.Children.Add(names);
            row.Child = dp;
            if (d.Hidden) row.Opacity = 0.5;
            Paint(row, false);
            row.MouseEnter += (s, e) => Paint(row, true);
            row.MouseLeave += (s, e) => Paint(row, false);
            row.MouseLeftButtonUp += (s, e) => Select(g);
            row.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { Select(g); Play(g); } };
            row.ContextMenu = Menu(g);
            return row;
        }

        // ---- the banner drifts slowly (off in Settings, and while Hub is hidden)
        void StartBannerMotion()
        {
            var st = new ScaleTransform(1, 1); var tt = new TranslateTransform();
            var tg = new TransformGroup(); tg.Children.Add(st); tg.Children.Add(tt);
            heroImg.RenderTransformOrigin = new Point(0.5, 0.5); heroImg.RenderTransform = tg;
            if (S.StillBanner || Snap.On) return;
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var zoom = new DoubleAnimation(1.0, 1.08, TimeSpan.FromSeconds(28)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
            var pan = new DoubleAnimation(-14, 14, TimeSpan.FromSeconds(34)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, zoom); st.BeginAnimation(ScaleTransform.ScaleYProperty, zoom); tt.BeginAnimation(TranslateTransform.XProperty, pan);
        }
        void StopBannerMotion()
        {
            var tg = heroImg.RenderTransform as TransformGroup; if (tg == null) return;
            foreach (var t in tg.Children) { t.BeginAnimation(ScaleTransform.ScaleXProperty, null); t.BeginAnimation(ScaleTransform.ScaleYProperty, null); t.BeginAnimation(TranslateTransform.XProperty, null); }
        }

        // ---- notes per game: a small pop-up editor under the Notes button
        Popup notesPop; TextBox notesBox; Game notesFor;
        void ShowNotes(UIElement anchor)
        {
            if (sel == null) return;
            if (notesPop == null)
            {
                notesBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = double.NaN, MinHeight = 150, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                notesBox.Background = B("#1A1522"); notesBox.Foreground = B("#ECE8F4"); notesBox.CaretBrush = B("#F07820"); notesBox.BorderThickness = new Thickness(0); notesBox.Padding = new Thickness(10, 8, 10, 8); notesBox.FontSize = 12.5;
                var sp = new StackPanel();
                var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                var ni = Icons.Make("note-sticky", 13, B("#F07820")); ni.Margin = new Thickness(0, 0, 8, 0); head.Children.Add(ni);
                head.Children.Add(new TextBlock { Text = "Notes", FontWeight = FontWeights.Bold, Foreground = B("#ECE8F4") });
                sp.Children.Add(head);
                sp.Children.Add(new Border { CornerRadius = new CornerRadius(8), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), Child = notesBox, ClipToBounds = true });
                sp.Children.Add(new TextBlock { Text = "Keybinds, loadouts, server codes... saved as you type.", Foreground = B("#8F88A0"), FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
                notesPop = new Popup { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom, VerticalOffset = 8,
                    Child = new Border { Width = 360, Background = B("#241E2E"), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 12), Child = sp } };
                notesBox.TextChanged += (s, e) => { if (notesFor != null) { S.For(notesFor.Id).Notes = notesBox.Text; } };
                notesPop.Closed += (s, e) => { S.Save(); ShowHero(); };
            }
            notesFor = sel; notesBox.Text = S.For(sel.Id).Notes ?? "";
            notesPop.PlacementTarget = anchor; notesPop.IsOpen = true;
            notesBox.Focus(); notesBox.CaretIndex = notesBox.Text.Length;
        }
    }
}
