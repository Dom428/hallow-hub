// Hallow Hub - v2.28: the launcher frame around the library. Page tabs on top (Library, Hallow apps, Stats, Storage, Sales),
// the game list on the left (Favorites, Recently played, your collections, All games), the pages drawn full size under the tabs,
// the "More" menus, and how it all fits a small window (the game list folds away under 1040 px).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace HallowHub
{
    public partial class HubWindow
    {
        ToggleButton libTab; Grid searchBox; Button sideToggle;
        ColumnDefinition sideCol; Border sideBar; StackPanel sideList; TextBox sideFilter; TextBlock sideHint; bool sideForced, sideNarrow;
        readonly HashSet<string> sideFolded = new HashSet<string>();
        readonly Dictionary<string, List<Border>> sideRows = new Dictionary<string, List<Border>>();
        FrameworkElement startChevron; TextBlock startSummary, todayText; Border startCard, dlFill; StackPanel dlBox; WrapPanel heroFacts;

        // ---------------------------------------------------------------- pages
        // a panel that used to float over the library becomes a page under the tabs: the page colour, no frame or shadow,
        // its content centred at the width it was made for
        Border AsPage(Border b, bool scroll)
        {
            var inner = b.Child; b.Child = null;
            double w = !double.IsNaN(b.Width) ? b.Width : !double.IsInfinity(b.MaxWidth) ? b.MaxWidth : 1040;
            b.ClearValue(WidthProperty); b.ClearValue(MaxWidthProperty);
            b.HorizontalAlignment = HorizontalAlignment.Stretch; b.VerticalAlignment = VerticalAlignment.Stretch;
            b.Margin = new Thickness(0, TopH, 0, BottomH); b.Padding = new Thickness(0);
            b.CornerRadius = new CornerRadius(0); b.BorderThickness = new Thickness(0); b.Effect = null; b.ClipToBounds = true;
            b.Background = (Brush)FindResource("Bg");
            var box = new Border { MaxWidth = Math.Max(w, 760) + 64, Padding = new Thickness(32, 22, 32, 12), Child = inner, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top };
            b.Child = scroll ? (UIElement)new ScrollViewer { Content = box, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } : box;
            DependencyPropertyDescriptor.FromProperty(VisibilityProperty, typeof(Border)).AddValueChanged(b, (s, e) => UpdateTabs());
            return b;
        }
        bool AnyPage()
        {
            foreach (var p in new[] { settingsPanel, statsPanel, storagePanel, familyPanel, newsPanel, wishPanel, aboutPanel })
                if (p != null && p.Visibility == Visibility.Visible) return true;
            return false;
        }
        void UpdateTabs()
        {
            if (libTab == null) return;
            libTab.IsChecked = !AnyPage();
            // the open page's tab is underlined, however it was opened (Sales is a plain button, the others toggle)
            foreach (var p in new[] { Tuple.Create((ButtonBase)familyBtn, familyPanel), Tuple.Create((ButtonBase)statsBtn, statsPanel), Tuple.Create((ButtonBase)storageBtn, storagePanel), Tuple.Create((ButtonBase)wishBtn, wishPanel) })
            {
                if (p.Item1 == null) continue;
                bool on = p.Item2 != null && p.Item2.Visibility == Visibility.Visible;
                if (on) { p.Item1.BorderBrush = B("#F07820"); p.Item1.Foreground = (Brush)FindResource("Text"); }
                else { p.Item1.ClearValue(Control.BorderBrushProperty); p.Item1.ClearValue(Control.ForegroundProperty); }
            }
        }
        void ShowLibrary()
        {
            newsPanel.Visibility = Visibility.Collapsed; aboutPanel.Visibility = Visibility.Collapsed; wishPanel.Visibility = Visibility.Collapsed;
            gearBtn.IsChecked = false; statsBtn.IsChecked = false; storageBtn.IsChecked = false; familyBtn.IsChecked = false;
            UpdateTabs();
        }

        // ---------------------------------------------------------------- the "More" menus
        MenuItem MI(string text, string icon, Action act)
        {
            var mi = new MenuItem { Header = text, Icon = icon == null ? null : Icons.Make(icon, 13, B("#ECE8F4")) };
            if (act != null) mi.Click += (s, e) => act();
            return mi;
        }
        // the top bar's: the rarer tools
        ContextMenu MoreMenu()
        {
            var m = new ContextMenu();
            m.Items.Add(MI("Big Picture  (F11)", "tv", OpenBigPicture));
            m.Items.Add(MI("Surprise me  (Ctrl+R)", "dice", () => Surprise()));
            var pick = MI("Pick one from...", "dice", null); var pm = PickMenu();
            foreach (var it in pm.Items.Cast<object>().ToList()) { pm.Items.Remove(it); pick.Items.Add(it); }
            if (pick.Items.Count > 0) m.Items.Add(pick);
            m.Items.Add(new Separator());
            m.Items.Add(MI("Storage", "hard-drive", () => storageBtn.IsChecked = true));
            if (S.SaleAlerts) m.Items.Add(MI("Sales and wishlist", "cart-shopping", OpenWishlist));
            m.Items.Add(MI("Emulators...", "compact-disc", OpenEmulators));
            if (Library.SteamPath() != null)
            {
                var sa = MI("Steam account", "steam", null);
                foreach (var it in SteamAccountChoices()) sa.Items.Add(it);
                m.Items.Add(sa);
            }
            m.Items.Add(new Separator());
            m.Items.Add(MI("Refresh library  (F5)", "arrows-rotate", () => { Rescan(); Status("Library refreshed - " + games.Count + " games."); }));
            m.Items.Add(MI("Check for updates", "download", () => CheckForUpdate(true)));
            m.Items.Add(MI("Settings", "gear", () => gearBtn.IsChecked = true));
            return m;
        }
        // the hero's: everything else you can do with this game (the right-click menu, plus its news and details)
        ContextMenu GameMenu(Game g)
        {
            var m = Menu(g);
            int at = 1;
            if (newsBtn != null && newsBtn.Visibility == Visibility.Visible) m.Items.Insert(at++, MI("News", "newspaper", OpenNews));
            if (aboutBtn != null && aboutBtn.Visibility == Visibility.Visible) m.Items.Insert(at++, MI("About this game", "circle-info", OpenAbout));
            m.Items.Insert(at, MI("Notes", "note-sticky", () => ShowNotes(notesBtn)));
            return m;
        }
        void OpenEmulators()
        {
            var w = new EmulatorsWindow(S.Emulators) { Owner = this };
            if (w.ShowDialog() == true) { S.Emulators = w.Result; S.Save(); Rescan(); Status(games.Count(x => x.Source == "rom") + " ROMs in your library from " + S.Emulators.Count + (S.Emulators.Count == 1 ? " emulator." : " emulators.")); }   // v2.9
        }

        // a click on a game (list, shelf or cover) opens it: selected, and the page scrolls up to its banner and Play
        void Open(Game g) { Select(g); if (gridScroll != null) gridScroll.ScrollToVerticalOffset(0); }

        // ---------------------------------------------------------------- the game list on the left
        Border BuildSidebar()
        {
            var b = new Border { Background = B("#1C1724"), Padding = new Thickness(0, 14, 0, 8) };
            var dp = new DockPanel();
            var fg = new Grid { Margin = new Thickness(12, 0, 12, 8) };
            sideFilter = new TextBox { Style = (Style)FindResource("Field"), Padding = new Thickness(22, 0, 0, 0), FontSize = 12, ToolTip = "Show only the games whose name has this" };
            var fi = Icons.Make("filter", 11, (Brush)FindResource("Muted")); fi.HorizontalAlignment = HorizontalAlignment.Left; fi.Margin = new Thickness(12, 0, 0, 0); fi.IsHitTestVisible = false;
            sideHint = new TextBlock { Text = "Filter by name", Foreground = (Brush)FindResource("Muted"), FontSize = 12, Margin = new Thickness(32, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            sideFilter.TextChanged += (s, e) => { sideHint.Visibility = sideFilter.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RebuildSide(); };
            fg.Children.Add(sideFilter); fg.Children.Add(fi); fg.Children.Add(sideHint);
            DockPanel.SetDock(fg, Dock.Top); dp.Children.Add(fg);
            sideList = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            dp.Children.Add(new ScrollViewer { Content = sideList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });
            b.Child = dp;
            return b;
        }
        void RebuildSide()
        {
            if (sideList == null) return;
            sideList.Children.Clear(); sideRows.Clear();
            var q = (sideFilter.Text ?? "").Trim();
            var pool = games.Where(g => (S.ShowHidden || !S.For(g.Id).Hidden) && (q.Length == 0 || g.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            Func<IEnumerable<Game>, List<Game>> az = l => l.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            SideGroup("fav", "FAVORITES", az(pool.Where(g => S.For(g.Id).Favorite)), true);
            SideGroup("recent", "RECENTLY PLAYED", pool.Where(g => LastOf(g) != null).OrderByDescending(g => LastOf(g)).Take(5).ToList(), false);
            foreach (var c in S.Collections) SideGroup("col:" + c, c.ToUpperInvariant(), az(pool.Where(g => S.For(g.Id).Tags.Contains(c))), true);
            SideGroup("all", "ALL GAMES", az(pool), true);
            if (pool.Count == 0) sideList.Children.Add(new TextBlock { Text = games.Count == 0 ? "No games yet." : "No game has that in its name.", Foreground = (Brush)FindResource("Muted"), FontSize = 12, Margin = new Thickness(18, 8, 12, 0), TextWrapping = TextWrapping.Wrap });
        }
        void SideGroup(string key, string title, List<Game> list, bool count)
        {
            if (list.Count == 0) return;
            bool folded = sideFolded.Contains(key);
            var head = new DockPanel { Margin = new Thickness(18, 12, 16, 4), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = folded ? "Show" : "Fold away" };
            if (count) { var n = new TextBlock { Text = list.Count.ToString(), FontSize = 10.5, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(n, Dock.Right); head.Children.Add(n); }
            var ch = Icons.Make(folded ? "plus" : "minus", 8, (Brush)FindResource("Muted")); ch.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(ch, Dock.Left); head.Children.Add(ch);
            head.Children.Add(new TextBlock { Text = title, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            head.MouseLeftButtonUp += (s, e) => { if (!sideFolded.Remove(key)) sideFolded.Add(key); RebuildSide(); };
            sideList.Children.Add(head);
            if (folded) return;
            foreach (var g in list) sideList.Children.Add(SideRow(g));
        }
        Border SideRow(Game g)
        {
            var d = S.For(g.Id);
            var row = new Border { Height = 30, Margin = new Thickness(8, 0, 8, 1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 0, 8, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = g, ToolTip = g.Name };
            var dp = new DockPanel();
            var art = Img(g.Cover, 64) ?? Img(g.Header, 96);
            FrameworkElement thumb;
            if (art != null) thumb = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(4), Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill } };
            else
            {
                var ic = ExeIcon(g.IconExe);
                thumb = ic != null ? (FrameworkElement)new Image { Source = ic, Width = 18, Height = 18, Margin = new Thickness(1) } :
                    new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(4), Background = B("#2E2639"), Child = Icons.Make(SrcIcon(g), 10, (Brush)FindResource("Muted")) };
            }
            thumb.Margin = new Thickness(0, 0, 10, 0); thumb.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(thumb, Dock.Left); dp.Children.Add(thumb);
            // what's going on with it: playing, updating, an update waiting
            FrameworkElement state = null;
            if (IsRunning(g)) state = Dot("#6ED28C", "Playing", "#6ED28C");
            else if (g.Updating) state = Dot("#5AB4F0", g.Progress >= 0 ? (int)(g.Progress * 100) + "%" : "Updating", "#5AB4F0");
            else if (g.NeedsUpdate) state = Dot("#F0BE5A", null, null);
            if (state != null) { state.Margin = new Thickness(6, 0, 0, 0); DockPanel.SetDock(state, Dock.Right); dp.Children.Add(state); }
            var name = new TextBlock { Text = g.Name, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "name" };
            dp.Children.Add(name);
            row.Child = dp;
            if (d.Hidden) row.Opacity = 0.5;
            PaintSide(row, false);
            row.MouseEnter += (s, e) => PaintSide(row, true);
            row.MouseLeave += (s, e) => PaintSide(row, false);
            row.MouseLeftButtonUp += (s, e) => Open(g);
            row.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { Select(g); Play(g); } };
            row.ContextMenu = new ContextMenu();
            row.ContextMenuOpening += (s, e) => row.ContextMenu = Menu(g);   // made when asked - the list can be long
            List<Border> l; if (!sideRows.TryGetValue(g.Id, out l)) sideRows[g.Id] = l = new List<Border>(); l.Add(row);
            return row;
        }
        FrameworkElement Dot(string col, string text, string textCol)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = BI(col), VerticalAlignment = VerticalAlignment.Center });
            if (text != null) sp.Children.Add(new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = B(textCol), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }
        void PaintSide(Border row, bool hot)
        {
            var g = row.Tag as Game; bool on = g != null && g == sel;
            row.Background = on ? B("#3A3248") : hot ? B("#2A2333") : Brushes.Transparent;
            var dp = row.Child as DockPanel; if (dp == null) return;
            foreach (var tb in dp.Children.OfType<TextBlock>())
                if ((tb.Tag as string) == "name") { tb.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal; tb.Foreground = on || hot ? (Brush)FindResource("Text") : B("#CFC8DC"); }
        }
        void PaintSideAll() { foreach (var l in sideRows.Values) foreach (var r in l) PaintSide(r, r.IsMouseOver); }
        static string SrcIcon(Game g) { return g.Source == "steam" ? "steam" : g.Source == "battlenet" ? "battle-net" : g.Source == "custom" ? "windows" : g.Source == "rom" ? "compact-disc" : g.Source == "xbox" ? "xbox" : "gamepad"; }

        // ---------------------------------------------------------------- small windows
        // the game list folds away under 1040 px (its button still opens it); the search box gets narrower
        double lastWidth;
        void FitWidth(double w)
        {
            if (w <= 0) return; lastWidth = w;
            sideNarrow = w < 1040;
            bool show = sideNarrow ? sideForced : !S.SideClosed;
            sideCol.Width = new GridLength(show ? 244 : 0);
            sideBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            sideToggle.ToolTip = show ? "Hide the game list" : "Show the game list";
            if (searchBox != null) searchBox.Width = w < 1100 ? 180 : 240;
            if (searchHint != null) searchHint.Text = w < 1100 ? "Search..." : "Search games, genres, notes...";
        }
        void ToggleSidebar()
        {
            bool shown = sideBar.Visibility == Visibility.Visible;
            if (sideNarrow) sideForced = !shown; else { S.SideClosed = shown; S.Save(); }
            FitWidth(lastWidth > 0 ? lastWidth : ActualWidth);
        }

        // ---------------------------------------------------------------- "When this game starts", folded or open
        void ShowStartSection()
        {
            if (startCard == null) return;
            startCard.Visibility = S.StartOpen ? Visibility.Visible : Visibility.Collapsed;
            var par = startChevron.Parent as Panel;
            if (par != null) { int i = par.Children.IndexOf(startChevron); var nc = Icons.Make(S.StartOpen ? "minus" : "plus", 10, (Brush)FindResource("Muted")); nc.Margin = startChevron.Margin; DockPanel.SetDock(nc, Dock.Left); par.Children.RemoveAt(i); par.Children.Insert(i, nc); startChevron = nc; }
            if (sel == null) { startSummary.Text = ""; return; }
            var d = S.For(sel.Id); var bits = new List<string>();
            bits.Add(d.Preset != "" ? "Hallow Mixer: " + d.Preset : "Mixer as it is");
            if (d.GameSession && Toolkit.CanSession) bits.Add("game session");
            int close = d.CloseFirst.Count(CloseApps.Allowed), open = d.OpenWith.Count;
            if (close > 0) bits.Add("closes " + close + (close == 1 ? " app" : " apps"));
            if (open > 0) bits.Add("opens " + open + (open == 1 ? " app" : " apps"));
            if (string.IsNullOrEmpty(d.PowerPlan) ? !string.IsNullOrEmpty(S.PowerPlanAll) : d.PowerPlan != "none") bits.Add("power plan");
            if (d.DailyLimit > 0) bits.Add("daily limit");
            startSummary.Text = string.Join("  ·  ", bits);
        }
    }
}
