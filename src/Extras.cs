// Hallow Hub v2.3 - library extras: hide and unhide games, and filters that combine (store, size, last played, playtime).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace HallowHub
{
    public partial class HubWindow
    {
        ToggleButton filterBtn; Popup filterPop; StackPanel filterBody;

        // ---- hide / unhide (tools, test servers, redistributables...) - nothing is uninstalled
        void ToggleHide(Game g)
        {
            if (g == null) return;
            var d = S.For(g.Id); d.Hidden = !d.Hidden; S.Save();
            RebuildChips(); RebuildGrid(); if (g == sel) ShowHero(); UpdateTrayMenu();
            Status(d.Hidden ? g.Name + " is hidden - it's under the Hidden filter if you want it back." : g.Name + " is back in your library.");
        }
        int HiddenCount() { return games.Count(g => S.For(g.Id).Hidden); }

        // ---- combined filters
        int FilterCount() { return (S.FStatus != "" ? 1 : 0) + (S.FTags.Count > 0 ? 1 : 0) + (S.FStores.Count > 0 ? 1 : 0) + (S.FSize != "" ? 1 : 0) + (S.FLast != "" ? 1 : 0) + (S.FTime != "" ? 1 : 0); }
        bool PassesFilters(Game g)
        {
            if (S.FStatus != "" && (S.FStatus == "none" ? (S.For(g.Id).Status ?? "") != "" : S.For(g.Id).Status != S.FStatus)) return false;   // v2.10
            if (S.FTags.Count > 0) { var tg = TagsOf(g).ToList(); if (!S.FTags.All(t => tg.Contains(t))) return false; }   // v2.8
            if (S.FStores.Count > 0 && !new[] { g }.Concat(g.Alts).Any(c => S.FStores.Contains(c.Source))) return false;
            if (S.FSize != "")
            {
                double gb = (g.SizeBytes + g.Alts.Sum(a => a.SizeBytes)) / (double)(1L << 30);
                if (S.FSize == "small" && !(gb > 0 && gb < 10)) return false;
                if (S.FSize == "mid" && !(gb >= 10 && gb <= 50)) return false;
                if (S.FSize == "big" && !(gb > 50)) return false;
            }
            if (S.FLast != "")
            {
                var last = LastOf(g); double days = last == null ? double.MaxValue : (DateTime.UtcNow - last.Value).TotalDays;
                if (S.FLast == "week" && days > 7) return false;
                if (S.FLast == "month" && days > 30) return false;
                if (S.FLast == "old" && (last == null || days <= 60)) return false;
                if (S.FLast == "never" && last != null) return false;
            }
            if (S.FTime != "")
            {
                var h = Total(g) / 60;
                if (S.FTime == "new" && h >= 1) return false;
                if (S.FTime == "some" && (h < 1 || h > 20)) return false;
                if (S.FTime == "lots" && h <= 20) return false;
            }
            return true;
        }

        FrameworkElement BuildFilterButton()
        {
            filterBtn = new ToggleButton { Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Filter by store, size, last played and playtime - together" };
            filterBody = new StackPanel();
            filterPop = new Popup { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom, VerticalOffset = 8, PlacementTarget = filterBtn,
                Child = new Border { Width = 380, Background = B("#241E2E"), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 14), Child = filterBody,
                    Effect = HallowLook.Shadow(24, 0.5, 4) } };
            filterBtn.Checked += (s, e) => { FillFilters(); filterPop.IsOpen = true; };
            filterBtn.Unchecked += (s, e) => filterPop.IsOpen = false;
            filterPop.Closed += (s, e) => { filterBtn.IsChecked = false; UpdateFilterButton(); };
            UpdateFilterButton();
            return filterBtn;
        }
        void UpdateFilterButton()
        {
            if (filterBtn == null) return;
            int n = FilterCount();
            filterBtn.Content = Icons.WithText("filter", n == 0 ? "Filters" : "Filters · " + n, 12, n == 0 || HallowLook.Light ? (Brush)FindResource("Text") : B("#F07820"));
        }
        void FillFilters()
        {
            filterBody.Children.Clear();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var clear = new Button { Content = "Clear", Style = (Style)FindResource("Btn"), Padding = new Thickness(10, 3, 10, 3), FontSize = 11.5, IsEnabled = FilterCount() > 0 };
            clear.Click += (s, e) => { S.FStores.Clear(); S.FTags.Clear(); S.FStatus = ""; S.FSize = ""; S.FLast = ""; S.FTime = ""; S.Save(); RebuildGrid(); FillFilters(); };
            DockPanel.SetDock(clear, Dock.Right); head.Children.Add(clear);
            head.Children.Add(new TextBlock { Text = "Show games that match all of these", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            filterBody.Children.Add(head);
            // stores: any number of them
            filterBody.Children.Add(FilterLabel("STORE"));
            var stores = new WrapPanel();
            foreach (var src in new[] { "steam", "battlenet", "epic", "ea", "ubisoft", "gog", "riot", "xbox", "custom", "rom" })
            {
                var one = AllCopies().FirstOrDefault(c => c.Source == src); if (one == null) continue;
                var key = src;
                var t = new ToggleButton { Content = one.SourceName, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 3, 10, 3), IsChecked = S.FStores.Contains(src) };
                t.Click += (s, e) => { if (t.IsChecked == true) { if (!S.FStores.Contains(key)) S.FStores.Add(key); } else S.FStores.Remove(key); S.Save(); RebuildGrid(); UpdateFilterButton(); };
                stores.Children.Add(t);
            }
            filterBody.Children.Add(stores);
            FilterChoice("SIZE", new[] { "", "Any", "small", "Under 10 GB", "mid", "10 to 50 GB", "big", "Over 50 GB" }, () => S.FSize, v => S.FSize = v);
            FilterChoice("LAST PLAYED", new[] { "", "Any time", "week", "This week", "month", "This month", "old", "Not in 2 months", "never", "Never" }, () => S.FLast, v => S.FLast = v);
            FilterChoice("PLAYTIME", new[] { "", "Any", "new", "Under 1 hour", "some", "1 to 20 hours", "lots", "Over 20 hours" }, () => S.FTime, v => S.FTime = v);
            FilterChoice("PLAY STATUS", new[] { "", "Any", "playing", "Playing", "plan", "Plan to play", "beaten", "Beaten", "completed", "Completed", "hold", "On hold", "abandoned", "Abandoned", "none", "No status" }, () => S.FStatus, v => S.FStatus = v);
            // v2.8: genres and features from the game details Hub has (fills in as you open games)
            var known = games.SelectMany(g => { var m = MetaOf(g); return m == null ? new List<string>() : m.Genres; }).Distinct().OrderBy(t => t).ToList();
            var feats = games.SelectMany(g => { var m = MetaOf(g); return m == null ? new List<string>() : m.Features; }).Distinct().OrderBy(t => t).ToList();
            foreach (var grp in new[] { Tuple.Create("GENRE", known), Tuple.Create("FEATURES", feats) })
            {
                if (grp.Item2.Count == 0 && S.FTags.Count == 0) continue;
                filterBody.Children.Add(FilterLabel(grp.Item1));
                var wp = new WrapPanel();
                foreach (var t0 in grp.Item2)
                {
                    var tag = t0;
                    var tb = new ToggleButton { Content = tag, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 3, 10, 3), IsChecked = S.FTags.Contains(tag) };
                    tb.Click += (s, e) => { if (tb.IsChecked == true) { if (!S.FTags.Contains(tag)) S.FTags.Add(tag); } else S.FTags.Remove(tag); S.Save(); RebuildGrid(); UpdateFilterButton(); };
                    wp.Children.Add(tb);
                }
                filterBody.Children.Add(wp);
            }
            if (known.Count == 0) filterBody.Children.Add(new TextBlock { Text = "Genres and features show up here as Hub gets each Steam game's details.", Foreground = (Brush)FindResource("Muted"), FontSize = 11, TextWrapping = TextWrapping.Wrap });
            filterBody.Children.Add(new TextBlock { Text = "Works with the filter chips, the search and the sort (Biggest first, Most played...).", Foreground = (Brush)FindResource("Muted"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        }
        TextBlock FilterLabel(string text) { return new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = B("#A98BE0"), Margin = new Thickness(0, 6, 0, 6) }; }
        // one-of choice: pairs of (value, label)
        void FilterChoice(string title, string[] opts, Func<string> get, Action<string> set)
        {
            filterBody.Children.Add(FilterLabel(title));
            var wp = new WrapPanel(); var all = new List<ToggleButton>();
            for (int i = 0; i + 1 < opts.Length; i += 2)
            {
                var val = opts[i];
                var t = new ToggleButton { Content = opts[i + 1], Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 3, 10, 3), IsChecked = get() == val };
                t.Click += (s, e) => { set(val); S.Save(); foreach (var x in all) x.IsChecked = x == t; RebuildGrid(); UpdateFilterButton(); };
                all.Add(t); wp.Children.Add(t);
            }
            filterBody.Children.Add(wp);
        }
    }
}
