// Hallow Hub v2.12 - "What should I play?" with a choice: anything, from your backlog (Plan to play, or installed and never
// started), a favourite, or something you haven't touched for a month; and a Your library section on the Stats page
// (play status split, backlog, finished games, your ratings) - like Playnite's statistics and GOG Galaxy's backlog view.
using System;
using System.Collections.Generic;
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
        public static readonly string[][] PickModes = {
            new[] { "any", "Anything (Ctrl+R)", "dice" }, new[] { "backlog", "From my backlog (Ctrl+Shift+R)", "bookmark" },
            new[] { "fav", "One of my favorites", "star" }, new[] { "old", "Something I haven't played in a month", "clock-rotate-left" } };

        // the backlog: games you plan to play, and installed games you've never started (unless you gave them another status)
        bool InBacklog(Game g)
        {
            var d = S.For(g.Id);
            return d.Status == "plan" || (string.IsNullOrEmpty(d.Status) && Total(g) < 1 && LastOf(g) == null);
        }
        static bool Finished(GameData d) { return d.Status == "beaten" || d.Status == "completed"; }

        ContextMenu PickMenu()
        {
            var m = new ContextMenu();
            foreach (var p in PickModes)
            {
                var key = p[0];
                var mi = new MenuItem { Header = p[1], Icon = Icons.Make(p[2], 13, B("#ECE8F4")) };
                mi.Click += (s, e) => Surprise(key);
                m.Items.Add(mi);
            }
            return m;
        }

        // "What should I play?" - favourites and games you haven't touched for a while come up more often
        readonly Random rng = new Random();
        void Surprise() { Surprise("any"); }
        public Game Surprise(string mode)
        {
            var basePool = games.Where(g => !S.For(g.Id).Hidden && !IsRunning(g) && g != sel);
            List<Game> pool; string empty, lead = "";
            if (mode == "backlog")
            {
                pool = basePool.Where(InBacklog).ToList(); lead = "From your backlog: ";
                empty = "Your backlog is empty - set a game to Plan to play (under Play), and games you've never started count too.";
            }
            else if (mode == "fav") { pool = basePool.Where(g => S.For(g.Id).Favorite).ToList(); lead = "A favorite: "; empty = "No favorites to pick from yet - star a game first."; }
            else if (mode == "old")
            {
                pool = basePool.Where(g => { var l = LastOf(g); return l != null && (DateTime.UtcNow - l.Value).TotalDays >= 30; }).ToList();
                lead = "Not played for a while: "; empty = "You've played everything in the last month - nothing to dig up.";
            }
            else { pool = basePool.Where(PassesFilters).ToList(); empty = "Nothing to pick from yet."; }   // v2.3: within your filters
            if (pool.Count == 0) { Status(empty); return null; }
            var weights = pool.Select(g =>
            {
                var d = S.For(g.Id); var last = LastOf(g); double days = last == null ? 30 : (DateTime.UtcNow - last.Value).TotalDays;
                if (mode == "backlog") return 1 + (d.Status == "plan" ? 2 : 0) + (d.Favorite ? 2 : 0) + (d.Rating > 0 ? 1 : 0);
                if (mode == "old") return 1 + Math.Min(days / 30, 6) + (Total(g) >= 120 ? 1 : 0);
                return 1 + (d.Favorite ? 2 : 0) + Math.Min(days / 7, 3) + (Total(g) >= 60 ? 1 : 0);
            }).ToList();
            double r = rng.NextDouble() * weights.Sum(); int i = 0;
            for (; i < pool.Count - 1; i++) { r -= weights[i]; if (r <= 0) break; }
            var pick = pool[i];
            if (!Visible().Contains(pick)) { S.Filter = "all"; if (search != null) search.Text = ""; RebuildChips(); RebuildGrid(); }
            Select(pick); Reveal(pick);
            var lastP = LastOf(pick); var st = S.For(pick.Id).Status;
            Status(lead + "How about " + pick.Name + "? " + (lastP == null ? "You haven't played it yet." : "Last played " + HubSettings.Ago(lastP).ToLowerInvariant() + ".") +
                (mode == "backlog" && st == "plan" ? " (on your Plan to play list)" : "") + "  Press Enter (or A) to play.");
            return pick;
        }

        // ---- Stats: Your library
        FrameworkElement LibrarySection()
        {
            var sp = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            var head = new DockPanel();
            var pickBtn = new Button { Style = (Style)FindResource("Btn"), Padding = new Thickness(10, 3, 12, 4), VerticalAlignment = VerticalAlignment.Top,
                Content = Icons.WithText("dice", "Pick one from my backlog", 11, B("#ECE8F4")), ToolTip = "Plan to play games, and installed games you've never started" };
            pickBtn.Click += (s, e) => { statsBtn.IsChecked = false; Surprise("backlog"); };
            DockPanel.SetDock(pickBtn, Dock.Right); head.Children.Add(pickBtn);
            head.Children.Add(SectionLabel("YOUR LIBRARY", "layer-group"));
            sp.Children.Add(head);
            var vis = games.Where(g => !S.For(g.Id).Hidden).ToList();
            if (vis.Count == 0) { sp.Children.Add(new TextBlock { Text = "Your games show up here once Hub finds them.", Foreground = (Brush)FindResource("Muted") }); return sp; }
            // the play status split, as one bar
            var order = new[] { "playing", "plan", "hold", "beaten", "completed", "abandoned", "" };
            var counts = order.Select(k => Tuple.Create(k, vis.Count(g => (S.For(g.Id).Status ?? "") == k))).Where(t => t.Item2 > 0).ToList();
            var bar = new Grid { Height = 12, Margin = new Thickness(0, 2, 0, 8) };
            bar.SizeChanged += (o, e) => bar.Clip = new RectangleGeometry(new Rect(0, 0, bar.ActualWidth, bar.ActualHeight), 6, 6);
            for (int i = 0; i < counts.Count; i++)
            {
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(counts[i].Item2, GridUnitType.Star) });
                var seg = new Border { Background = B(counts[i].Item1 == "" ? "#3A3248" : StatusColor(counts[i].Item1)), Margin = new Thickness(0, 0, i < counts.Count - 1 ? 2 : 0, 0),
                    ToolTip = StatusName(counts[i].Item1) + ": " + counts[i].Item2 };
                Grid.SetColumn(seg, i); bar.Children.Add(seg);
            }
            sp.Children.Add(bar);
            var legend = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            foreach (var c in counts)
            {
                var k = c.Item1;
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 4), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = "Show these games" };
                item.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = B(k == "" ? "#5A5268" : StatusColor(k)), Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center });
                item.Children.Add(new TextBlock { Text = StatusName(k) + "  " + c.Item2, Foreground = B("#CFC8DC"), FontSize = 11.5 });
                item.MouseLeftButtonUp += (o, e) => { statsBtn.IsChecked = false; S.FStatus = k == "" ? "none" : k; S.Save(); RebuildChips(); RebuildGrid(); Status("Showing " + StatusName(k).ToLowerInvariant() + " games - clear it under Filters."); };
                legend.Children.Add(item);
            }
            sp.Children.Add(legend);
            // backlog, finished, ratings
            int plan = vis.Count(g => S.For(g.Id).Status == "plan"), never = vis.Count(g => InBacklog(g) && S.For(g.Id).Status != "plan");
            int started = vis.Count(g => Total(g) >= 1 || LastOf(g) != null || (S.For(g.Id).Status ?? "") != "" && S.For(g.Id).Status != "plan");
            int done = vis.Count(g => Finished(S.For(g.Id)));
            var rated = vis.Where(g => S.For(g.Id).Rating > 0).ToList();
            var tiles = new UniformGrid { Rows = 1, Margin = new Thickness(-5, 0, -5, 0) };
            Action<string, string, string, string> tile = (label, value, col, sub) =>
            {
                var t = new StackPanel();
                t.Children.Add(new TextBlock { Text = label, Foreground = (Brush)FindResource("Muted"), FontSize = 11.5 });
                t.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = B(col), Margin = new Thickness(0, 2, 0, 0) });
                t.Children.Add(new TextBlock { Text = sub, Foreground = (Brush)FindResource("Muted"), FontSize = 10.5, TextWrapping = TextWrapping.Wrap });
                tiles.Children.Add(new Border { Child = t, Background = B("#2A2333"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(5, 0, 5, 0) });
            };
            tile("Backlog", (plan + never) + (plan + never == 1 ? " game" : " games"), "#CFC8DC", plan + " planned, " + never + " never started");
            tile("Finished", done + (done == 1 ? " game" : " games"), "#5AB4F0", started > 0 ? Math.Round(100.0 * done / started) + "% of the games you've started" : "Beaten or Completed");
            tile("Your ratings", rated.Count == 0 ? "-" : rated.Average(g => S.For(g.Id).Rating).ToString("0.0") + " \u2605", "#F0BE5A",
                rated.Count == 0 ? "Rate games with the stars under Play" : rated.Count + " rated - top: " + rated.OrderByDescending(g => S.For(g.Id).Rating).ThenByDescending(Total).First().Name);
            tile("Collections", S.Collections.Count.ToString(), "#C9B2F5", S.Collections.Count == 0 ? "Make one in Game settings" : string.Join(", ", S.Collections.Take(3)) + (S.Collections.Count > 3 ? "..." : ""));
            sp.Children.Add(tiles);
            return sp;
        }
    }
}
