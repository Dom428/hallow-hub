// Hallow Hub v2.3 - the Storage page in Big Picture, made for the controller: drives, clean-up suggestions first,
// up/down to move, A to uninstall through the game's own launcher (asks here first, then the launcher asks again), B back.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace HallowHub
{
    public partial class HubWindow
    {
        Grid bsView; Border bsAskBox; StackPanel bsList, bsDrives; TextBlock bsSub, bsAskTitle, bsAskHow, bsNote; ScrollViewer bsScroll;
        readonly List<Game> bsRows = new List<Game>(); readonly List<Border> bsTiles = new List<Border>(); int bsIdx; Game bsAsk;
        bool BsOpen { get { return bsView != null && bsView.Visibility == Visibility.Visible; } }

        FrameworkElement BuildBigStorage()
        {
            bsView = new Grid { Background = B("#0E0B12"), Visibility = Visibility.Collapsed };
            var lay = new Grid { Margin = new Thickness(110, 46, 110, 40) };
            foreach (var h in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) lay.RowDefinitions.Add(new RowDefinition { Height = h });
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            var tl = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Icons.Make("hard-drive", 30, B("#F07820")); ic.Margin = new Thickness(0, 0, 16, 0); tl.Children.Add(ic);
            tl.Children.Add(new TextBlock { Text = "Storage", FontSize = 38, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(tl);
            bsSub = new TextBlock { FontSize = 18, Foreground = B("#F0BE5A"), Margin = new Thickness(2, 8, 0, 0) }; head.Children.Add(bsSub);
            Grid.SetRow(head, 0); lay.Children.Add(head);
            bsDrives = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 22) };
            Grid.SetRow(bsDrives, 1); lay.Children.Add(bsDrives);
            bsList = new StackPanel();
            bsScroll = new ScrollViewer { Content = bsList, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            Grid.SetRow(bsScroll, 2); lay.Children.Add(bsScroll);
            var foot = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            bsNote = new TextBlock { FontSize = 16, Foreground = B("#6ED28C"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
            foot.Children.Add(bsNote);
            foot.Children.Add(new TextBlock { FontSize = 14, Foreground = B("#8F88A0"), HorizontalAlignment = HorizontalAlignment.Center,
                Text = "Up / Down  move     A / Enter  uninstall through its launcher (asks first)     B / Esc  back to your games" });
            Grid.SetRow(foot, 3); lay.Children.Add(foot);
            bsView.Children.Add(lay);
            // the "are you sure?" box
            bsAskBox = new Border { Width = 720, Background = B("#211B2A"), BorderBrush = BI("#F0BE5A"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(18), Padding = new Thickness(34, 28, 34, 28),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Effect = HallowLook.Shadow(50, 0.8, 0) };
            var ask = new StackPanel();
            bsAskTitle = new TextBlock { FontSize = 26, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
            bsAskHow = new TextBlock { FontSize = 17, Foreground = B("#CFC8DC"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), LineHeight = 26 };
            ask.Children.Add(bsAskTitle); ask.Children.Add(bsAskHow);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 24, 0, 0) };
            btns.Children.Add(PadButton("A", "#6ED28C", "Yes, uninstall", "#F07820", () => BsAnswer(true)));
            btns.Children.Add(PadButton("B", "#F05A5A", "Keep it", "#3A3248", () => BsAnswer(false)));
            ask.Children.Add(btns);
            bsAskBox.Child = ask;
            bsView.Children.Add(bsAskBox);
            return bsView;
        }
        // a big button with the controller letter on it
        Border PadButton(string letter, string letterCol, string text, string bg, Action click)
        {
            var b = new Border { CornerRadius = new CornerRadius(12), Background = BF(bg), Padding = new Thickness(20, 10, 26, 10), Margin = new Thickness(0, 0, 14, 0), Cursor = Cursors.Hand };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = B(letterCol), Margin = new Thickness(0, 0, 12, 0),
                Child = new TextBlock { Text = letter, FontWeight = FontWeights.Bold, FontSize = 16, Foreground = B("#0E0B12"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            sp.Children.Add(new TextBlock { Text = text, FontSize = 19, FontWeight = FontWeights.Bold, Foreground = bg == "#F07820" ? B("#1A0E04") : HallowLook.Light ? (Brush)FindResource("Text") : Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            b.Child = sp; b.MouseLeftButtonUp += (s, e) => click();
            return b;
        }

        void BsShow()
        {
            if (bsView == null) return;
            bsView.Visibility = Visibility.Visible; bsIdx = 0; bsAsk = null; bsAskBox.Visibility = Visibility.Collapsed; bsNote.Text = "";
            afterSizing = () => { if (BsOpen) BsFill(); };
            FillStorage();   // starts measuring games their launcher doesn't size
            BsFill();
        }
        void BsHide()
        {
            if (bsView == null) return;
            bsView.Visibility = Visibility.Collapsed; bsAsk = null; afterSizing = null;
        }
        void BsFill()
        {
            var copies = AllCopies().Where(c => !string.IsNullOrEmpty(c.InstallDir) && Directory.Exists(c.InstallDir)).ToList();
            var tips = CleanupTips();
            bsSub.Text = tips.Count > 0 ? "You could free up " + Size(tips.Sum(t => t.Item1.SizeBytes)) + " - suggestions first" : sizing ? "Measuring games their launcher doesn't size..." : "Nothing to clean up - everything's been played lately";
            // drives
            bsDrives.Children.Clear();
            foreach (var grp in copies.GroupBy(c => (c.Drive ?? Path.GetPathRoot(c.InstallDir).TrimEnd('\\')).ToUpperInvariant()).OrderBy(x => x.Key))
            {
                DriveInfo di = null; try { di = new DriveInfo(grp.Key); if (!di.IsReady) di = null; } catch { }
                long onDrive = grp.Sum(c => Math.Max(0, c.SizeBytes));
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = grp.Key + "   " + grp.Count() + (grp.Count() == 1 ? " game, " : " games, ") + (Size(onDrive) ?? "0 GB"), FontSize = 17, FontWeight = FontWeights.SemiBold });
                if (di != null && di.TotalSize > 0)
                {
                    double t = di.TotalSize, gFrac = Math.Min(1, onDrive / t), uFrac = Math.Max(0, Math.Min(1 - gFrac, (di.TotalSize - di.AvailableFreeSpace - onDrive) / t));
                    var bar = new Grid { Height = 12, Margin = new Thickness(0, 8, 0, 6) };
                    bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, gFrac), GridUnitType.Star) });
                    bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, uFrac), GridUnitType.Star) });
                    bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - gFrac - uFrac), GridUnitType.Star) });
                    var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                    fill.GradientStops.Add(new GradientStop(C("#FF9A45"), 0)); fill.GradientStops.Add(new GradientStop(C("#F05A8C"), 1));
                    var g2 = new Border { Background = B("#5A5268") }; var g3 = new Border { Background = B("#2E2639"), CornerRadius = new CornerRadius(0, 6, 6, 0) };
                    Grid.SetColumn(g2, 1); Grid.SetColumn(g3, 2);
                    bar.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(6, 0, 0, 6) }); bar.Children.Add(g2); bar.Children.Add(g3);
                    sp.Children.Add(bar);
                    sp.Children.Add(new TextBlock { Text = Size(di.AvailableFreeSpace) + " free of " + Size(di.TotalSize), FontSize = 14, Foreground = B("#8F88A0") });
                }
                bsDrives.Children.Add(new Border { Width = 360, Margin = new Thickness(0, 0, 20, 0), Padding = new Thickness(18, 14, 18, 14), CornerRadius = new CornerRadius(14), Background = B("#1C1724"), Child = sp });
            }
            // rows: suggestions first, then everything else by size
            var keep = bsIdx >= 0 && bsIdx < bsRows.Count ? bsRows[bsIdx] : null;
            bsRows.Clear(); bsTiles.Clear(); bsList.Children.Clear();
            var why = new Dictionary<Game, string>();
            foreach (var t in tips) if (!why.ContainsKey(t.Item1)) { why[t.Item1] = t.Item2; bsRows.Add(t.Item1); }
            foreach (var c in copies.OrderByDescending(x => x.SizeBytes)) if (!why.ContainsKey(c)) bsRows.Add(c);
            foreach (var c in bsRows)
            {
                var main = Main(c); var tip = why.ContainsKey(c);
                var row = new Border { Height = 78, Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Background = B("#1A1522"), Padding = new Thickness(12, 8, 22, 8), Cursor = Cursors.Hand };
                var dp = new DockPanel();
                var size = new TextBlock { Text = c.SizeBytes > 0 ? Size(c.SizeBytes) : (sizing ? "..." : "?"), FontSize = 24, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Width = 120, TextAlignment = TextAlignment.Right };
                DockPanel.SetDock(size, Dock.Right); dp.Children.Add(size);
                var art = Img(main.Header, 300) ?? Img(main.Cover, 200);
                var th = new Border { Width = 128, Height = 58, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 18, 0), Background = art != null ? (Brush)new ImageBrush(art) { Stretch = Stretch.UniformToFill } : B("#2E2639") };
                DockPanel.SetDock(th, Dock.Left); dp.Children.Add(th);
                var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                txt.Children.Add(new TextBlock { Text = main.Name + (main.Alts.Count > 0 ? "  (" + c.SourceName + ")" : "") + "   ·   " + (c.Drive ?? ""), FontSize = 20, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                if (tip) { var bi = Icons.Make("broom", 13, BI("#F0BE5A")); bi.Margin = new Thickness(0, 0, 8, 0); sub.Children.Add(bi); }
                var rl = RowLast(c);
                sub.Children.Add(new TextBlock { Text = tip ? why[c] : (rl == null ? "no playtime recorded" : "last played " + AgoLow(rl)) + "   ·   uninstalls through " + c.SourceName, FontSize = 14, Foreground = tip ? B("#F0BE5A") : B("#8F88A0"), TextTrimming = TextTrimming.CharacterEllipsis });
                txt.Children.Add(sub);
                dp.Children.Add(txt);
                row.Child = dp;
                var cc = c;
                row.MouseLeftButtonUp += (s, e) => { int i = bsRows.IndexOf(cc); if (i >= 0) BsFocus(i); };
                row.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { int i = bsRows.IndexOf(cc); if (i >= 0) { BsFocus(i); BsAskNow(); } } };
                bsTiles.Add(row); bsList.Children.Add(row);
            }
            if (bsRows.Count == 0) bsList.Children.Add(new TextBlock { Text = "No installed games found.", FontSize = 18, Foreground = B("#8F88A0") });
            int k = keep == null ? -1 : bsRows.FindIndex(x => x.Id == keep.Id);
            BsFocus(k >= 0 ? k : Math.Min(bsIdx, Math.Max(0, bsRows.Count - 1)));
        }
        void BsFocus(int i)
        {
            if (bsRows.Count == 0) return;
            bsIdx = Math.Max(0, Math.Min(bsRows.Count - 1, i));
            for (int k = 0; k < bsTiles.Count; k++)
            {
                var on = k == bsIdx; var ac = Accent(Main(bsRows[k]));
                bsTiles[k].BorderBrush = on ? new SolidColorBrush(ac) : Brushes.Transparent;
                bsTiles[k].Background = on ? B("#2A2333") : B("#1A1522");
                bsTiles[k].Effect = on ? HallowLook.Glow(ac, 26, 0.5) : null;
            }
            bsTiles[bsIdx].BringIntoView();
        }
        void BsAskNow()
        {
            if (bsRows.Count == 0) return;
            var c = bsRows[bsIdx];
            if (tracker.Now.ContainsKey(c.Id)) { bsNote.Foreground = B("#F0BE5A"); bsNote.Text = Main(c).Name + " is running - close it first."; return; }
            bsAsk = c;
            bsAskTitle.Text = "Uninstall " + UninstallTitle(c) + "?";
            bsAskHow.Text = UninstallHow(c) + "\nYour playtime in Hub is kept.";
            bsAskBox.Visibility = Visibility.Visible;
        }
        void BsAnswer(bool yes)
        {
            var c = bsAsk; bsAsk = null; bsAskBox.Visibility = Visibility.Collapsed;
            if (!yes || c == null) return;
            StartUninstall(c);
            bsNote.Foreground = B("#6ED28C");
            bsNote.Text = c.Source == "steam" || c.Source == "battlenet" ? c.SourceName + "'s uninstaller is open - finish it there (it may be behind this screen: B, then Alt+Tab)." : statusText.Text;
        }
        // keyboard and controller while Storage is open; true = handled
        bool BsKey(Key k)
        {
            if (bsAsk != null)
            {
                if (k == Key.Enter) { BsAnswer(true); return true; }
                if (k == Key.Escape || k == Key.Back) { BsAnswer(false); return true; }
                return true;
            }
            switch (k)
            {
                case Key.Up: BsFocus(bsIdx - 1); return true;
                case Key.Down: BsFocus(bsIdx + 1); return true;
                case Key.Enter: BsAskNow(); return true;
                case Key.Escape: case Key.Back: case Key.S: BsHide(); return true;
            }
            return false;
        }
        void BsPad(ushort b)
        {
            if (bsAsk != null)
            {
                if ((b & Pad.A) != 0) BsAnswer(true);
                else if ((b & Pad.B) != 0) BsAnswer(false);
                return;
            }
            if ((b & Pad.Up) != 0) BsFocus(bsIdx - 1);
            if ((b & Pad.Down) != 0) BsFocus(bsIdx + 1);
            if ((b & Pad.A) != 0) BsAskNow();
            if ((b & Pad.B) != 0 || (b & Pad.Start) != 0) BsHide();
        }
    }
}
