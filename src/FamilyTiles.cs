// Hallow Hub v2.14 - more of the family on the main page, as small cards next to Saves / Clips / Overlay:
//   Guard  - the safety score, the weekly report's change and its fixed / new counts, and hardening warnings
//   Shield - ads and trackers blocked today and this week (from Hallow Shield's bridge, 1.6+)
//   Assist - your own PCs with an online dot, and Connect (Assist's "connect <name>": it opens Assist with that PC picked)
// All read from the apps' status.json; each card only shows when its app is there (and new enough to share that). Nothing here can stop Hub.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HallowHub
{
    public class GuardWeek
    {
        public bool Running; public int Score = -1; public bool Family; public string Label;
        public bool HasReport; public int? Delta; public int Fixed, NewIssues, Issues; public string Week, Headline;
        public int HardScore = -1, HardWarnings = -1;
    }
    public class ShieldBrowser { public string Name, Profile, Level; public long? Today, Week, Banners; }
    public class ShieldInfo { public bool Sharing; public long? Today, Week, Banners; public DateTime? Updated; public string Version; public List<ShieldBrowser> Browsers = new List<ShieldBrowser>(); }
    public class AssistPc { public string Name, Online; public DateTime? Checked; }

    public partial class HubWindow
    {
        static long? Count(Dictionary<string, object> d, string k)
        {
            var s = Family.Str(d, k); if (s == null) return null; double v = Family.Num(d, k); return v < 0 ? (long?)null : (long)Math.Round(v);
        }

        // ---- readers (static, so the tests use them on stand-in files)
        public static GuardWeek ReadGuardWeek()
        {
            if (!Family.Installed("Guard")) return null;
            var st = Family.Status("Guard"); if (st == null) return null;
            var g = new GuardWeek { Running = Family.Running("Guard") };
            if (Family.Str(st, "familyScore") != null) { g.Score = (int)Math.Round(Family.Num(st, "familyScore")); g.Family = true; g.Label = Family.Str(st, "familyLabel"); }
            else if (Family.Str(st, "score") != null) { g.Score = (int)Math.Round(Family.Num(st, "score")); g.Label = Family.Str(st, "scoreLabel"); }
            object ro; Dictionary<string, object> r = st.TryGetValue("lastReport", out ro) ? ro as Dictionary<string, object> : null;
            if (r != null)
            {
                g.HasReport = true; g.Week = Family.Str(r, "week"); g.Headline = Family.Str(r, "headline");
                if (Family.Str(r, "delta") != null) g.Delta = (int)Math.Round(Family.Num(r, "delta"));
                g.Fixed = (int)Family.Num(r, "fixed"); g.NewIssues = (int)Family.Num(r, "newIssues"); g.Issues = (int)Family.Num(r, "issues");
            }
            if (Family.Str(st, "hardeningScore") != null) g.HardScore = (int)Math.Round(Family.Num(st, "hardeningScore"));
            // a warning count, when Guard shares one ("hardeningWarnings", or "hardening": {"warnings"})
            if (Family.Str(st, "hardeningWarnings") != null) g.HardWarnings = (int)Family.Num(st, "hardeningWarnings");
            else { object ho; var h = st.TryGetValue("hardening", out ho) ? ho as Dictionary<string, object> : null; if (h != null && Family.Str(h, "warnings") != null) g.HardWarnings = (int)Family.Num(h, "warnings"); }
            return g;
        }
        // Shield is a browser extension: "there" = its bridge wrote status.json, or Hallow Guard found it in a browser
        public static bool ShieldThere()
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(Family.DataDir("Shield"), "status.json"))) return true;
            var gs = Family.Installed("Guard") ? Family.Status("Guard") : null; object fo;
            var fam = gs != null && gs.TryGetValue("family", out fo) ? fo as Dictionary<string, object> : null;
            var sh = Family.Str(fam, "shield");
            return sh != null && sh != "missing";
        }
        public static ShieldInfo ReadShield()
        {
            if (!ShieldThere()) return null;
            var st = Family.Status("Shield"); var s = new ShieldInfo();
            if (st == null) return s;   // there, but not sharing counts
            s.Sharing = true; s.Version = Family.Str(st, "version"); s.Updated = Family.Time(st, "updated");
            s.Today = Count(st, "blockedToday"); s.Week = Count(st, "blockedWeek"); s.Banners = Count(st, "bannersToday");
            foreach (var b in Family.List(st, "browsers"))
                s.Browsers.Add(new ShieldBrowser { Name = Family.Str(b, "name"), Profile = Family.Str(b, "profile"), Level = Cap(Family.Str(b, "level")), Today = Count(b, "blockedToday"), Week = Count(b, "blockedWeek"), Banners = Count(b, "bannersToday") });
            // totals missing but browsers there: add them up
            if (s.Today == null && s.Browsers.Any(b => b.Today != null)) s.Today = s.Browsers.Sum(b => b.Today ?? 0);
            if (s.Week == null && s.Browsers.Any(b => b.Week != null)) s.Week = s.Browsers.Sum(b => b.Week ?? 0);
            return s;
        }
        // null = Assist isn't installed, or too old to list your PCs (no "pcs")
        public static List<AssistPc> ReadAssistPcs()
        {
            if (!Family.Installed("Assist")) return null;
            var st = Family.Status("Assist"); object po;
            if (st == null || !st.TryGetValue("pcs", out po) || !(po is System.Collections.ArrayList)) return null;
            var r = new List<AssistPc>();
            foreach (var p in Family.List(st, "pcs"))
            {
                var name = Family.Str(p, "name"); if (string.IsNullOrWhiteSpace(name)) continue;
                var on = Family.Str(p, "online"); if (on != "waiting" && on != "busy" && on != "offline") on = "unknown";
                r.Add(new AssistPc { Name = Clean(name), Online = on, Checked = Family.Time(p, "checked") });
            }
            return r;
        }
        static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }
        static string Clean(string s) { return new string((s ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim(); }
        public static string N(long? n) { return n == null ? "-" : n.Value.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture); }

        // ---- the cards
        FrameworkElement GuardTile(GuardWeek g)
        {
            StackPanel body; var card = FamilyCard("shield-halved", "SAFETY", "Hallow Guard", out body);
            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -3, 0, 2) };
            var col = g.Score < 0 ? "#8F88A0" : g.Score < 50 ? "#F05A5A" : g.Score < 75 ? "#F0BE5A" : "#6ED28C";
            top.Children.Add(new TextBlock { Text = g.Score < 0 ? "-" : g.Score.ToString(), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = B(col), VerticalAlignment = VerticalAlignment.Center });
            if (g.Delta != null && g.Delta != 0)
                top.Children.Add(new TextBlock { Text = (g.Delta > 0 ? "  \u25B2 +" : "  \u25BC ") + g.Delta + " this week", FontSize = 11.5, Foreground = B(g.Delta > 0 ? "#6ED28C" : "#F0BE5A"), VerticalAlignment = VerticalAlignment.Center });
            else top.Children.Add(new TextBlock { Text = "  " + (g.HasReport ? (g.Delta == null ? "first weekly report" : "same as last week") : (g.Label ?? "")), FontSize = 11.5, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center });
            body.Children.Add(top);
            var parts = new List<string>();
            if (g.HasReport) { if (g.Fixed > 0) parts.Add(g.Fixed + " fixed"); if (g.NewIssues > 0) parts.Add(g.NewIssues + " new"); if (g.Fixed == 0 && g.NewIssues == 0) parts.Add(g.Issues == 0 ? "nothing to fix" : g.Issues + (g.Issues == 1 ? " thing" : " things") + " to look at"); }
            string hard = null; int hl = 0;
            if (g.HardWarnings > 0) { hard = g.HardWarnings + (g.HardWarnings == 1 ? " hardening warning" : " hardening warnings"); hl = 1; }
            else if (g.HardWarnings == 0) hard = "hardening OK";
            else if (g.HardScore >= 0 && g.HardScore < 100) { hard = "hardening " + g.HardScore + "/100"; hl = g.HardScore < 70 ? 1 : 0; }
            else if (g.HardScore == 100) hard = "hardening OK";
            // the second line: hardening first (the warning count when Guard shares one), else the report's fixed / new
            var line = Line(hard ?? string.Join(", ", parts), true);
            if (hard != null && hl > 0) line.Foreground = B("#F0BE5A");
            body.Children.Add(line);
            card.Cursor = Cursors.Hand;
            card.ToolTip = (g.Headline ?? (g.Family ? "Family safety score" : "Safety score") + " " + (g.Score < 0 ? "-" : g.Score.ToString())) + (g.Week != null ? " (week " + g.Week + ")" : "") + (parts.Count > 0 ? "\nThis week: " + string.Join(", ", parts) : "") +
                (g.Family ? "\nYour PC and the Hallow apps together" : "") + (g.HardScore >= 0 ? "\nWindows hardening: " + g.HardScore + "/100" : "") + (g.Running ? "" : "\nHallow Guard isn't running - this is from its last check") + "\nClick to open the weekly report in Hallow Guard";
            card.MouseLeftButtonUp += (s, e) => { if (Family.Running("Guard")) FamilySend("Guard", "open reports", "Opened Hallow Guard's weekly report.", "Hallow Guard didn't open"); else StartApp("Guard"); };
            return card;
        }

        public const string ShieldHowTo = "Shield 1.6+: options page > Stats > turn on \"Share counts with Hallow apps on this PC\" (Hallow Setup 1.8 sets up its bridge).";
        FrameworkElement ShieldTile(ShieldInfo s)
        {
            StackPanel body; var card = FamilyCard("eye-slash", "BLOCKED", "Hallow Shield", out body);
            if (!s.Sharing)
            {
                body.Children.Add(Line("Shield isn't sharing counts", false));
                var how = Line(ShieldHowTo, true); how.TextWrapping = TextWrapping.Wrap; how.TextTrimming = TextTrimming.None; how.FontSize = 10.5; how.MaxHeight = 30;
                body.Children.Add(how); card.ToolTip = ShieldHowTo;
                return card;
            }
            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -3, 0, 2) };
            top.Children.Add(new TextBlock { Text = N(s.Today), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = B("#6ED28C"), VerticalAlignment = VerticalAlignment.Center });
            top.Children.Add(new TextBlock { Text = "  today", FontSize = 11.5, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center });
            body.Children.Add(top);
            var old = s.Updated != null && (DateTime.Now - s.Updated.Value).TotalHours > 24;
            body.Children.Add(Line(N(s.Week) + " this week" + (old ? ", as of " + AgoText(s.Updated.Value) : ""), true));
            card.ToolTip = "Ads and trackers Hallow Shield blocked" + (s.Banners != null ? "\n" + N(s.Banners) + " cookie banners answered today" : "") + (s.Browsers.Count > 0 ? "\n" + string.Join("\n", s.Browsers.Select(b => (b.Name ?? "Browser") + (string.IsNullOrEmpty(b.Profile) ? "" : " (" + b.Profile + ")") + ": " + N(b.Today) + " today, " + N(b.Week) + " this week" + (string.IsNullOrEmpty(b.Level) ? "" : " - " + b.Level))) : "") +
                (s.Updated != null ? "\nUpdated " + AgoText(s.Updated.Value) : "");
            return card;
        }

        FrameworkElement AssistTile(List<AssistPc> pcs)
        {
            StackPanel body; var card = FamilyCard("tv", "YOUR PCS", "Hallow Assist", out body);
            if (pcs.Count == 0) { body.Children.Add(Line("No PCs of your own in Hallow Assist yet.", true)); return card; }
            bool running = Family.Running("Assist");
            var show = pcs.OrderBy(p => p.Online == "waiting" ? 0 : p.Online == "busy" ? 1 : p.Online == "unknown" ? 2 : 3).Take(2).ToList();
            foreach (var p in show)
            {
                var row = new DockPanel { Margin = new Thickness(0, -2, 0, 3) };
                var pc = p;
                var go = new Button { Content = "Connect", Style = (Style)FindResource("Btn"), Padding = new Thickness(8, 1, 9, 2), FontSize = 11, Margin = new Thickness(6, 0, 0, 0),
                    IsEnabled = p.Online != "offline", ToolTip = !running ? "Starts Hallow Assist - then click Connect again" : p.Online == "offline" ? pc.Name + " is offline" : "Open Hallow Assist with " + pc.Name + " picked" };
                go.Click += (s, e) =>
                {
                    if (!Family.Running("Assist")) { StartApp("Assist"); Status("Starting Hallow Assist - click Connect again in a moment."); return; }
                    go.IsEnabled = false; FamilySend("Assist", "connect " + pc.Name, "Hallow Assist is open with " + pc.Name + " picked.", "Hallow Assist couldn't open " + pc.Name);
                };
                DockPanel.SetDock(go, Dock.Right); row.Children.Add(go);
                var dotCol = p.Online == "waiting" ? "#6ED28C" : p.Online == "busy" ? "#F0BE5A" : "#5A5366";
                var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = BI(dotCol), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = p.Online == "waiting" ? "Online" : p.Online == "busy" ? "Online - in a session" : p.Online == "offline" ? "Offline" : "Not checked yet" };
                if (p.Online == "unknown") { dot.Fill = Brushes.Transparent; dot.Stroke = B("#8F88A0"); dot.StrokeThickness = 1.2; }
                DockPanel.SetDock(dot, Dock.Left); row.Children.Add(dot);
                row.Children.Add(new TextBlock { Text = p.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = p.Name + (p.Checked != null ? "\nChecked " + AgoText(p.Checked.Value) : "") });
                body.Children.Add(row);
            }
            if (pcs.Count > show.Count) card.ToolTip = string.Join("\n", pcs.Select(p => p.Name + " - " + (p.Online == "waiting" ? "online" : p.Online == "busy" ? "in a session" : p.Online)));
            return card;
        }
    }
}
