// Hallow Hub v2.22 - Real-world checks on the Family page: a "Real-world checks" button next to "Run checkup", the list (green
// = it worked, red = it didn't, grey = not yet), each with "Try it" (opens the right app on the right page - only when you
// click), "Worked" and "Didn't work". Hub looks at the apps' status files every 2 minutes (and every 10 seconds while the list
// is open) so a check turns green by itself. The pipe's "realchecks" answers with the list (for Hallow Remote and for fixes).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowHub
{
    public partial class HubWindow
    {
        StackPanel realHost; bool realShown; Dictionary<string, RealMark> realMarks; DispatcherTimer realFast;

        Button BuildRealChecksButton()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Icons.Make("list", 12, B("#F07820")); ic.Margin = new Thickness(0, 0, 7, 0); ic.VerticalAlignment = VerticalAlignment.Center; sp.Children.Add(ic);
            sp.Children.Add(new TextBlock { Text = "Real-world checks", VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = sp, Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 4, 12, 4),
                ToolTip = "The things only your real PC, games and phone can prove - each turns green by itself when the app shows it worked" };
            b.Click += (s, e) => { realShown = !realShown; LookRealChecks(); ShowRealChecks(); };
            return b;
        }

        string RealFile() { return RealChecks.File_(CheckEnv.Default()); }
        void LookRealChecks()
        {
            try
            {
                var env = CheckEnv.Default();
                if (realMarks == null) realMarks = RealChecks.Load(RealFile());
                if (RealChecks.Look(env, realMarks, DateTime.Now) && !Snap.On) RealChecks.Save(RealFile(), realMarks);
            }
            catch { }
        }

        // every 2 minutes in the background (status files only - cheap), every 10 seconds while the list is open
        void StartRealChecksTimer()
        {
            if (Snap.On) return;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            t.Tick += (s, e) => { if (!exiting) { LookRealChecks(); if (realShown) ShowRealChecks(); } };
            t.Start();
            realFast = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            realFast.Tick += (s, e) => { if (!realShown || exiting || familyPanel == null || familyPanel.Visibility != Visibility.Visible) return; var before = Ser(); LookRealChecks(); if (Ser() != before) ShowRealChecks(); };
            realFast.Start();
        }
        string Ser() { return realMarks == null ? "" : new JavaScriptSerializer().Serialize(RealChecks.ToJson(realMarks)); }

        string RealChecksCommand()
        {
            string j = null;
            if (!OnUi(() => { LookRealChecks(); j = new JavaScriptSerializer().Serialize(RealChecks.ToJson(realMarks ?? new Dictionary<string, RealMark>())); })) return "error busy";
            return "ok " + j;
        }

        void MarkReal(string id, string state)
        {
            if (realMarks == null) realMarks = RealChecks.Load(RealFile());
            RealMark m; if (!realMarks.TryGetValue(id, out m)) { m = new RealMark(); realMarks[id] = m; }
            m.State = state; m.How = state == "" ? "" : "you"; m.At = state == "" ? "" : DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            if (!Snap.On) RealChecks.Save(RealFile(), realMarks);
            ShowRealChecks();
        }

        void ShowRealChecks()
        {
            if (realHost == null) return;
            realHost.Children.Clear();
            if (!realShown) return;
            if (realMarks == null) realMarks = new Dictionary<string, RealMark>();
            var box = new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(12), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 12, 10), Margin = new Thickness(0, 4, 0, 6) };
            var sp = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var close = new Button { Content = "x", Tag = "icon", Style = (Style)FindResource("Btn"), Width = 28, Height = 26, Padding = new Thickness(0), ToolTip = "Hide the list" };
            close.Click += (s, e) => { realShown = false; ShowRealChecks(); };
            DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
            var hi = Icons.Make("list", 14, B("#F07820")); hi.Margin = new Thickness(0, 0, 9, 0); hi.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(hi, Dock.Left); head.Children.Add(hi);
            var c = RealChecks.Counts(realMarks); int pass = (int)c["passed"], fail = (int)c["failed"], total = (int)c["total"];
            var ht = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            ht.Inlines.Add(new System.Windows.Documents.Run("Real-world checks") { FontWeight = FontWeights.Bold, FontSize = 14 });
            ht.Inlines.Add(new System.Windows.Documents.Run("   " + pass + " of " + total + " worked" + (fail > 0 ? ", " + fail + " didn't" : "")) { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = fail > 0 ? B("#F05A5A") : pass == total ? B("#6ED28C") : B("#F0BE5A") });
            head.Children.Add(ht);
            sp.Children.Add(head);
            sp.Children.Add(new TextBlock { Text = "Things only your real PC, games and phone can prove. Each one turns green by itself when the app shows it worked - or mark it yourself.", FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 6) });
            foreach (var ch in RealChecks.All) { RealMark m; realMarks.TryGetValue(ch.Id, out m); sp.Children.Add(RealRow(ch, m ?? new RealMark())); }
            box.Child = sp;
            realHost.Children.Add(box);
        }

        FrameworkElement RealRow(RealCheck ch, RealMark m)
        {
            int lv = m.State == "pass" ? 0 : m.State == "fail" ? 2 : -1;
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = Icons.Make(lv == 0 ? "circle-check" : lv == 2 ? "circle-xmark" : "clock", 13, LevelBrush(lv)); ic.VerticalAlignment = VerticalAlignment.Top; ic.Margin = new Thickness(2, 2, 0, 0); g.Children.Add(ic);
            var area = new TextBlock { Text = ch.App.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = B("#A98BE0"), Margin = new Thickness(0, 3, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(area, 1); g.Children.Add(area);
            var txt = new StackPanel();
            txt.Children.Add(new TextBlock { Text = ch.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            string detail;
            DateTime at; var when = DateTime.TryParse(m.At, out at) ? at.ToString("ddd d MMM, HH:mm") : "";
            if (m.State == "pass") detail = (m.How == "auto" ? "Seen: " + m.Seen : "You marked it as working") + (when != "" ? "  ·  " + when : "");
            else if (m.State == "fail") detail = (m.How == "auto" ? "Seen: " + m.Seen : "You marked it as not working - tell Claude what happened") + (when != "" ? "  ·  " + when : "");
            else detail = ch.HowTo + (m.Seen != "" ? "  (" + m.Seen + ")" : "");
            txt.Children.Add(new TextBlock { Text = detail, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = m.State == "fail" ? B("#F05A5A") : (Brush)FindResource("Muted"), Margin = new Thickness(0, 1, 0, 0) });
            Grid.SetColumn(txt, 2); g.Children.Add(txt);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
            Func<string, string, bool, Button> mk = (label, tip, accent) => new Button { Content = label, Style = (Style)FindResource(accent ? "Accent" : "Btn"), Padding = new Thickness(9, 3, 10, 3), FontSize = 11.5, Margin = new Thickness(6, 0, 0, 0), ToolTip = tip };
            var tryIt = mk(ch.TryLabel, "Opens Hallow " + ch.TryApp + " where you can try it", m.State != "pass");
            var fix = new CheckFix(ch.TryLabel, "open", ch.TryApp, ch.TryArg, null);
            tryIt.Click += (s, e) => RunCheckFix(fix, tryIt);
            btns.Children.Add(tryIt);
            if (m.State == "")
            {
                var ok = mk("Worked", "Mark it as working", false); ok.Click += (s, e) => MarkReal(ch.Id, "pass"); btns.Children.Add(ok);
                var bad = mk("Didn't work", "Mark it as not working", false); bad.Click += (s, e) => MarkReal(ch.Id, "fail"); btns.Children.Add(bad);
            }
            else { var again = mk("Reset", "Back to not checked yet", false); again.Click += (s, e) => MarkReal(ch.Id, ""); btns.Children.Add(again); }
            Grid.SetColumn(btns, 3); g.Children.Add(btns);
            return g;
        }

        // --snap (test family only): the list with a few results, and its json
        void SnapRealChecks(string dir)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST"))) return;
            realMarks = new Dictionary<string, RealMark>(); RealChecks.Look(CheckEnv.Default(), realMarks, new DateTime(2026, 11, 24, 20, 15, 0));
            realShown = true; familyBtn.IsChecked = true; ShowRealChecks();
            var keepMax = familyScroll.MaxHeight; familyScroll.MaxHeight = 3000; Snap.Save(this, Path.Combine(dir, "real-checks.png"), 1180, 1100); familyScroll.MaxHeight = keepMax;
            File.WriteAllText(Path.Combine(dir, "real-checks.txt"), new JavaScriptSerializer().Serialize(RealChecks.ToJson(realMarks)));
            realShown = false; ShowRealChecks(); familyBtn.IsChecked = false;
        }
    }
}
