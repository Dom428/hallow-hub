// Hallow Hub v2.10 - play status (Playing, Beaten, Completed, On hold, Abandoned, Plan to play) and your own rating per game,
// as filters and sorts; and a search that looks past the name: genres, features, developer, notes, collections, status.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HallowHub
{
    public partial class HubWindow
    {
        public static readonly string[][] Statuses = {
            new[] { "", "No status" }, new[] { "plan", "Plan to play" }, new[] { "playing", "Playing" }, new[] { "beaten", "Beaten" },
            new[] { "completed", "Completed" }, new[] { "hold", "On hold" }, new[] { "abandoned", "Abandoned" } };
        static int StatusOrder(string k) { var o = new[] { "playing", "plan", "hold", "beaten", "completed", "abandoned", "" }; var i = Array.IndexOf(o, k ?? ""); return i < 0 ? 99 : i; }
        static string StatusName(string k) { var s = Statuses.FirstOrDefault(x => x[0] == (k ?? "")); return s == null ? "" : s[1]; }
        static string StatusColor(string k) { return k == "playing" ? "#6ED28C" : k == "beaten" ? "#5AB4F0" : k == "completed" ? "#F0BE5A" : k == "hold" ? "#A98BE0" : k == "abandoned" ? "#F05A5A" : k == "plan" ? "#CFC8DC" : "#8F88A0"; }

        StackPanel statusRow; ComboBox statusBox; StackPanel starRow;
        FrameworkElement BuildStatusRow()
        {
            statusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            statusBox = new ComboBox { Style = (Style)FindResource("Combo"), Width = 150, ToolTip = "Where you are with this game" };
            foreach (var s in Statuses) statusBox.Items.Add(new ComboBoxItem { Content = s[1], Tag = s[0] });
            statusBox.SelectionChanged += (o, e) =>
            {
                if (loading || sel == null) return; var it = statusBox.SelectedItem as ComboBoxItem; if (it == null) return;
                S.For(sel.Id).Status = (string)it.Tag; S.Save(); RefreshCard(sel);
                if (S.FStatus != "") RebuildGrid();
                Status(S.For(sel.Id).Status == "" ? sel.Name + " has no play status." : sel.Name + ": " + StatusName(S.For(sel.Id).Status) + ".");
            };
            statusRow.Children.Add(statusBox);
            starRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, ToolTip = "Your rating - click a star (click it again to clear)" };
            for (int i = 1; i <= 5; i++)
            {
                var n = i;
                var st = new Border { Width = 22, Height = 22, Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = Icons.Make("star", 15, B("#5A5268")) };
                st.MouseLeftButtonUp += (o, e) => { if (sel == null) return; var d = S.For(sel.Id); d.Rating = d.Rating == n ? 0 : n; S.Save(); ShowStatus(sel); if (S.Sort == "rating") RebuildGrid(); };
                starRow.Children.Add(st);
            }
            statusRow.Children.Add(starRow);
            return statusRow;
        }
        void ShowStatus(Game g)
        {
            if (statusRow == null || g == null) return;
            var d = S.For(g.Id);
            var was = loading; loading = true;
            statusBox.SelectedItem = statusBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (d.Status ?? "")) ?? statusBox.Items[0];
            loading = was;
            for (int i = 0; i < starRow.Children.Count; i++)
                ((Border)starRow.Children[i]).Child = Icons.Make("star", 15, i < d.Rating ? BI("#F0BE5A") : B("#5A5268"));
        }
        // a game you planned to play counts as Playing once it starts
        void StatusOnStart(Game g) { var d = S.For(g.Id); if (d.Status == "plan") d.Status = "playing"; }

        // ---- search: every word must match somewhere; genre: dev: status: col: note: store: narrow it to one place
        bool SearchMatch(Game g, string q)
        {
            if (q.Length == 0) return true;
            var d = S.For(g.Id); var m = MetaOf(g);
            foreach (var word in q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var w = word; string field = null; int c = w.IndexOf(':');
                if (c > 0 && c < w.Length - 1) { field = w.Substring(0, c).ToLowerInvariant(); w = w.Substring(c + 1); }
                Func<string, bool> has = s => !string.IsNullOrEmpty(s) && s.IndexOf(w, StringComparison.CurrentCultureIgnoreCase) >= 0;
                var genres = m == null ? new List<string>() : m.Tags.ToList();
                bool ok;
                if (field == "genre" || field == "tag") ok = genres.Any(has);
                else if (field == "dev") ok = m != null && (has(m.Developer) || has(m.Publisher));
                else if (field == "status") ok = has(StatusName(d.Status)) || has(d.Status);
                else if (field == "col") ok = d.Tags.Any(has);
                else if (field == "note") ok = has(d.Notes);
                else if (field == "store") ok = new[] { g }.Concat(g.Alts).Any(x => has(x.SourceName) || has(x.Platform));
                else
                {
                    if (field != null) w = word;   // not one of ours ("Half-Life: Alyx"): the whole word
                    ok = has(g.Name) || has(g.OriginalName) || genres.Any(has) || (m != null && (has(m.Developer) || has(m.Publisher))) || d.Tags.Any(has) || has(d.Notes)
                        || has(StatusName(d.Status)) || has(g.Platform) || has(g.SourceName);
                }
                if (!ok) return false;
            }
            return true;
        }
    }
}
