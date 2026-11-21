// Hallow Hub v2.13 - Hallow Guard and Hallow Assist in Hub: what Settings says about them (read from their status.json), Guard's
// safety score as one small pill next to the PC check, and a picture of the Hallow icon's menu for --snap. Nothing here can stop Hub
// working: a missing, old or broken app just leaves its line out.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WF = System.Windows.Forms;

namespace HallowHub
{
    public partial class HubWindow
    {
        // Guard's status.json: score (0-100 or null), scoreLabel, lastScan, threats (Defender's history count, null when hidden)
        public class GuardInfo { public bool Running; public int Score = -1; public string Label; public DateTime? LastScan; public int Threats = -1; public DateTime Updated; }
        public static GuardInfo ReadGuard()
        {
            if (!Family.Installed("Guard")) return null;
            var st = Family.Status("Guard"); if (st == null) return null;
            var g = new GuardInfo { Running = Family.Running("Guard"), Label = Family.Str(st, "scoreLabel"), LastScan = Family.Time(st, "lastScan"), Updated = Family.StatusTime("Guard") };
            if (Family.Str(st, "score") != null) g.Score = (int)Math.Round(Math.Max(0, Math.Min(100, Family.Num(st, "score"))));
            if (Family.Str(st, "threats") != null) g.Threats = (int)Family.Num(st, "threats");
            return g;
        }

        // one line per installed app, for Settings (under Hallow Mixer / Hallow Toolkit)
        public static string GuardLine(GuardInfo g)
        {
            if (g == null) return Family.Installed("Guard") ? "Hallow Guard: " + (Family.Running("Guard") ? "starting up..." : "not running") : null;
            var parts = new List<string>();
            parts.Add(g.Score >= 0 ? "safety score " + g.Score + (string.IsNullOrEmpty(g.Label) ? "" : " (" + g.Label + ")") : "no safety score yet");
            parts.Add(g.LastScan == null ? "no scan yet" : "last scan " + AgoText(g.LastScan.Value));
            if (g.Threats == 0) parts.Add("no threats in Defender's history");
            else if (g.Threats > 0) parts.Add(g.Threats + (g.Threats == 1 ? " threat" : " threats") + " in Defender's history");
            return "Hallow Guard: " + (g.Running ? "" : "not running - ") + string.Join(", ", parts);
        }
        public static string AssistLine()
        {
            if (!Family.Installed("Assist")) return null;
            if (!Family.Running("Assist")) return "Hallow Assist: not running";
            var st = Family.Status("Assist");
            var session = Family.Str(st, "session");
            var what = Family.Bool(st, "helping") ? "you're helping someone" : session == "connected" ? "a helper is connected" : session == "asking" ? "someone is asking to connect" : session == "waiting" ? "waiting for a helper" : "no session";
            return "Hallow Assist: running - " + what;
        }
        static string AgoText(DateTime local)
        {
            var d = DateTime.Now - local;
            if (d.TotalMinutes < 2) return "just now";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + " minutes ago";
            if (d.TotalHours < 24) return (int)d.TotalHours + (d.TotalHours < 2 ? " hour ago" : " hours ago");
            if (d.TotalDays < 2) return "yesterday";
            return (int)d.TotalDays + " days ago";
        }
        string FamilyAppLines()
        {
            var lines = new List<string>();
            try { var l = GuardLine(ReadGuard()); if (l != null) lines.Add(l); } catch { }
            try { var l = AssistLine(); if (l != null) lines.Add(l); } catch { }
            return lines.Count == 0 ? "" : "\n" + string.Join("\n", lines);
        }

        // the small pill next to CPU / RAM / GPU: "Safety 91" (amber under 75, red under 50; amber with threats in the history)
        void AddGuardPill()
        {
            GuardInfo g; try { g = ReadGuard(); } catch { return; }
            if (g == null || g.Score < 0) return;
            int level = g.Score < 50 ? 2 : g.Score < 75 || g.Threats > 0 ? 1 : 0;
            var col = level == 2 ? "#F05A5A" : level == 1 ? "#F0BE5A" : "#CFC8DC";
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Icons.Make("shield-halved", 10, B(col)); ic.Margin = new Thickness(0, 0, 5, 0); sp.Children.Add(ic);
            sp.Children.Add(new TextBlock { Text = "Safety " + g.Score + (g.Threats > 0 ? "  ·  " + g.Threats + (g.Threats == 1 ? " threat" : " threats") : ""), FontSize = 11, Foreground = B(col) });
            var b = new Border { Child = sp, Margin = new Thickness(0, 0, 10, 4), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                ToolTip = "Hallow Guard: " + (g.Label ?? "safety score") + " (" + g.Score + "/100)\n" + (g.LastScan == null ? "No scan yet" : "Last scan " + AgoText(g.LastScan.Value)) +
                    (g.Threats >= 0 ? "\n" + (g.Threats == 0 ? "No threats" : g.Threats + (g.Threats == 1 ? " threat" : " threats")) + " in Defender's history" : "") +
                    (g.Running ? "" : "\nAs of " + AgoText(g.Updated.ToLocalTime()) + " - Hallow Guard isn't running") + "\nClick to open Hallow Guard" };
            b.MouseLeftButtonUp += (s, e) => { if (Family.Running("Guard")) FamilySend("Guard", "open", "Opened Hallow Guard.", "Hallow Guard didn't open"); else StartApp("Guard"); };
            pcPills.Children.Add(b);
        }

        // --snap: the Hallow icon's menu as Hub would show it, drawn to a PNG (main menu, then each app's submenu beside it)
        void SnapTrayMenu(string path)
        {
            var menu = new WF.ContextMenuStrip();
            var open = new WF.ToolStripMenuItem("Open Hallow Hub"); open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold); menu.Items.Add(open);
            menu.Items.Add(new WF.ToolStripMenuItem("Play")); menu.Items.Add(new WF.ToolStripMenuItem("Big Picture (full screen)"));
            menu.Items.Add(new WF.ToolStripSeparator());
            var quit = new WF.ToolStripMenuItem("Quit"); menu.Items.Add(quit);
            var keepQuit = quitItem; quitItem = quit;
            try { UpdateFamilyMenu(menu); } finally { quitItem = keepQuit; }
            var shots = new List<System.Drawing.Bitmap>();
            Func<WF.ToolStripDropDown, System.Drawing.Bitmap> draw = dd =>
            {
                dd.PerformLayout(); var sz = dd.GetPreferredSize(System.Drawing.Size.Empty); dd.Size = sz;
                var bmp = new System.Drawing.Bitmap(Math.Max(1, sz.Width), Math.Max(1, sz.Height));
                dd.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height)); return bmp;
            };
            shots.Add(draw(menu));
            foreach (WF.ToolStripItem it in menu.Items) { var mi = it as WF.ToolStripMenuItem; if (mi != null && mi.DropDownItems.Count > 0 && mi != open) shots.Add(draw(mi.DropDown)); }
            int w = shots.Sum(x => x.Width) + 12 * (shots.Count + 1), h = shots.Max(x => x.Height) + 24;
            using (var all = new System.Drawing.Bitmap(w, h))
            {
                using (var gr = System.Drawing.Graphics.FromImage(all))
                {
                    gr.Clear(System.Drawing.Color.FromArgb(0x1A, 0x16, 0x21)); int x = 12;
                    foreach (var s in shots) { gr.DrawImage(s, x, 12); x += s.Width + 12; }
                }
                all.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            foreach (var s in shots) s.Dispose();
            menu.Dispose();
        }
    }
}
