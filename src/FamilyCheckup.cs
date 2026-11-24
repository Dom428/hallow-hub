// Hallow Hub v2.21 - Family checkup: the Family page's "Run checkup" button, the list it shows (OK / look at this / problem,
// each with its one-click fix or the steps), a quiet weekly run (never during a game; one note only when something needs a
// look, held until the game is over), status.json "checkup" and the pipe's "checkup" for Hallow Remote. The checks
// themselves are in Checkup.cs. Every fix runs only when you click it.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace HallowHub
{
    public partial class HubWindow
    {
        StackPanel checkupHost; Button checkupBtn; CheckupResult checkup; volatile bool checkupRunning; string checkupNote, checkupDone; bool checkupHidden;
        DateTime startedAt = DateTime.Now;

        Button BuildCheckupButton()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Icons.Make("stethoscope", 12, B("#F07820")); ic.Margin = new Thickness(0, 0, 7, 0); ic.VerticalAlignment = VerticalAlignment.Center; sp.Children.Add(ic);
            sp.Children.Add(new TextBlock { Text = "Run checkup", VerticalAlignment = VerticalAlignment.Center });
            checkupBtn = new Button { Content = sp, Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 4, 12, 4),
                ToolTip = "Check the whole Hallow family: versions, running apps, settings stuck in Claude's sandbox, backups, notifications, Ransomware protection, Start with Windows" };
            checkupBtn.Click += (s, e) => { checkupHidden = false; RunCheckup(false); };
            return checkupBtn;
        }

        // the real PC (or the test family) plus what Hub's restarter knows: the apps you usually run
        CheckEnv CheckupEnv()
        {
            var env = CheckEnv.Default();
            var fileUsual = env.Usual;
            env.Usual = () =>
            {
                var l = new List<string>(fileUsual() ?? new List<string>());
                if (restarter != null)
                {
                    l.AddRange(restarter.Usual);
                    foreach (var w in restarter.Snapshot())
                    {
                        DateTime t;
                        if (w.State == "running" || (DateTime.TryParse(w.LastSeenRunning, null, System.Globalization.DateTimeStyles.RoundtripKind, out t) && (DateTime.UtcNow - t.ToUniversalTime()).TotalDays < 14)) l.Add(w.App);
                    }
                }
                return l.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            };
            if (Snap.On) { env.Send = (a, l) => null; env.Start = (e, a) => false; }
            return env;
        }

        // quiet = the weekly run (a note only when something needs a look); otherwise the list opens on the Family page
        void RunCheckup(bool quiet)
        {
            if (checkupRunning) return;
            checkupRunning = true; if (!quiet) ShowCheckup();
            var env = CheckupEnv();
            new Thread(() =>
            {
                CheckupResult r = null;
                try { r = Checkup.Run(env); } catch { }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    checkupRunning = false;
                    if (r != null)
                    {
                        checkup = r;
                        S.CheckupLast = r.At.ToString("o"); S.CheckupProblems = r.Problems; S.CheckupWarnings = r.Warnings; S.Save();
                        lastStatus = null; WriteFamilyStatus(true);
                        if (quiet && r.Problems + r.Warnings > 0) { checkupNote = "Family checkup: " + r.Summary().ToLowerInvariant() + " - see Hub's Family page."; FlushCheckupNote(); }
                    }
                    ShowCheckup();
                }));
            }) { IsBackground = true, Name = "Hallow family checkup", Priority = ThreadPriority.BelowNormal }.Start();
        }

        // weekly, quietly: 10 minutes after Hub starts at the earliest, never while a game runs or a full-screen app is in front
        void StartCheckupTimer()
        {
            if (Snap.On) return;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            t.Tick += (s, e) =>
            {
                try
                {
                    FlushCheckupNote();
                    if (checkupRunning || exiting) return;
                    if (Checkup.Due(S.CheckupWeekly, S.CheckupLast, DateTime.Now, (DateTime.Now - startedAt).TotalMinutes, GameOn())) RunCheckup(true);
                }
                catch { }
            };
            t.Start();
        }
        void FlushCheckupNote()
        {
            if (checkupNote == null || GameOn()) return;
            var n = checkupNote; checkupNote = null;
            Status(n);
            if (tray != null && tray.Visible) tray.ShowBalloonTip(6000, "Hallow Hub", n, WF.ToolTipIcon.None);
        }

        // status.json / the pipe: counts only
        Dictionary<string, object> CheckupStatus()
        {
            DateTime last; var has = DateTime.TryParse(S.CheckupLast, null, System.Globalization.DateTimeStyles.RoundtripKind, out last);
            return new Dictionary<string, object> { { "lastRun", has ? last.ToString("yyyy-MM-ddTHH:mm:ss") : null }, { "problems", has ? (object)S.CheckupProblems : null }, { "warnings", has ? (object)S.CheckupWarnings : null } };
        }
        // pipe "checkup" (the last result: ids, levels, plain titles) and "checkup run" (starts one in the background)
        string CheckupCommand(string arg)
        {
            if (arg.Trim().Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                if (Snap.On) return "error not now";
                bool started = false; if (!OnUi(() => { if (!checkupRunning) { RunCheckup(true); started = true; } })) return "error busy";
                return "ok {\"started\":" + (started ? "true" : "false") + ",\"running\":true}";
            }
            if (arg.Trim().Length > 0) return "error usage: checkup [run]";
            Dictionary<string, object> d = null; bool running = false;
            if (!OnUi(() => { running = checkupRunning; d = checkup != null ? checkup.ToJson() : CheckupStatus(); })) return "error busy";
            d["running"] = running;
            return "ok " + new JavaScriptSerializer().Serialize(d);
        }

        static string LevelIcon(int lv) { return lv == 2 ? "circle-xmark" : lv == 1 ? "triangle-exclamation" : lv == 0 ? "circle-check" : "circle-info"; }
        SolidColorBrush LevelBrush(int lv) { return lv == 2 ? BI("#F05A5A") : lv == 1 ? BI("#F0BE5A") : lv == 0 ? BI("#6ED28C") : B("#8F88A0"); }

        void ShowCheckup()
        {
            if (checkupHost == null) return;
            checkupHost.Children.Clear();
            if (checkupHidden || (checkup == null && !checkupRunning)) return;
            var box = new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(12), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 12, 10), Margin = new Thickness(0, 4, 0, 6) };
            var sp = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var close = new Button { Content = "x", Tag = "icon", Style = (Style)FindResource("Btn"), Width = 28, Height = 26, Padding = new Thickness(0), ToolTip = "Hide the checkup" };
            close.Click += (s, e) => { checkupHidden = true; ShowCheckup(); };
            DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
            if (checkup != null && !checkupRunning)
            {
                var again = new Button { Content = "Check again", Style = (Style)FindResource("Btn"), Padding = new Thickness(10, 3, 10, 3), FontSize = 11.5, Margin = new Thickness(0, 0, 6, 0) };
                again.Click += (s, e) => RunCheckup(false);
                DockPanel.SetDock(again, Dock.Right); head.Children.Add(again);
            }
            var hi = Icons.Make("stethoscope", 14, B("#F07820")); hi.Margin = new Thickness(0, 0, 9, 0); hi.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(hi, Dock.Left); head.Children.Add(hi);
            var ht = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            ht.Inlines.Add(new System.Windows.Documents.Run("Family checkup") { FontWeight = FontWeights.Bold, FontSize = 14 });
            if (checkupRunning) ht.Inlines.Add(new System.Windows.Documents.Run("   Checking the family...") { FontSize = 12, Foreground = (Brush)FindResource("Muted") });
            else if (checkup != null)
            {
                int worst = checkup.Problems > 0 ? 2 : checkup.Warnings > 0 ? 1 : 0;
                ht.Inlines.Add(new System.Windows.Documents.Run("   " + checkup.Summary()) { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = worst == 0 ? (Brush)B("#6ED28C") : worst == 1 ? B("#F0BE5A") : B("#F05A5A") });
                ht.Inlines.Add(new System.Windows.Documents.Run("  ·  " + checkup.At.ToString("ddd d MMM, HH:mm")) { FontSize = 11.5, Foreground = (Brush)FindResource("Muted") });
            }
            head.Children.Add(ht);
            sp.Children.Add(head);
            if (checkupDone != null) sp.Children.Add(new TextBlock { Text = checkupDone, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 6), Foreground = (Brush)FindResource("Text") });
            if (checkup != null)
            {
                foreach (var area in Checkup.Areas)
                    foreach (var it in checkup.Items.Where(i => i.Area == area).OrderByDescending(i => i.Level))
                        sp.Children.Add(CheckRow(it));
            }
            box.Child = sp;
            checkupHost.Children.Add(box);
        }

        FrameworkElement CheckRow(CheckItem it)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = Icons.Make(LevelIcon(it.Level), 13, LevelBrush(it.Level)); ic.VerticalAlignment = VerticalAlignment.Top; ic.Margin = new Thickness(2, 2, 0, 0); g.Children.Add(ic);
            var area = new TextBlock { Text = it.Area.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = B("#A98BE0"), Margin = new Thickness(0, 3, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(area, 1); g.Children.Add(area);
            var txt = new StackPanel();
            txt.Children.Add(new TextBlock { Text = it.Title, FontSize = 12.5, FontWeight = it.Level > 0 ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Foreground = it.Level < 0 ? (Brush)FindResource("Muted") : (Brush)FindResource("Text") });
            if (!string.IsNullOrEmpty(it.Detail)) txt.Children.Add(new TextBlock { Text = it.Detail, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 1, 0, 0) });
            Grid.SetColumn(txt, 2); g.Children.Add(txt);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
            foreach (var f in it.Fixes)
            {
                var fx = f;
                var bt = new Button { Content = f.Label, Style = (Style)FindResource(f.Kind == "bring-back" || f.Kind == "update" ? "Accent" : "Btn"), Padding = new Thickness(9, 3, 10, 3), FontSize = 11.5, Margin = new Thickness(6, 0, 0, 0), ToolTip = f.Tip };
                bt.Click += (s, e) => RunCheckFix(fx, bt);
                btns.Children.Add(bt);
            }
            Grid.SetColumn(btns, 3); g.Children.Add(btns);
            return g;
        }

        void CheckAgainSoon(double seconds)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            t.Tick += (s, e) => { t.Stop(); RunCheckup(false); };
            t.Start();
        }

        void RunCheckFix(CheckFix f, Button bt)
        {
            if (Snap.On) return;
            if ((f.Kind == "start" || f.Kind == "open" || f.Kind == "bring-back") && CheckEnv.InAppPackage()) { Status("Hub is running inside Claude's sandbox - quit it and open it from the Start menu first, so the apps start on your PC."); return; }
            var name = "Hallow " + f.App;
            switch (f.Kind)
            {
                case "update":
                    if (f.App == "Hub" && pending != null) { InstallUpdate(); return; }
                    if (Family.Installed("Setup"))
                    {
                        try { Process.Start(new ProcessStartInfo(Family.Exe("Setup"), "--update " + f.App) { UseShellExecute = true }); Status("Hallow Setup is updating " + name + " - you can watch it there."); CheckAgainSoon(60); return; }
                        catch { }
                    }
                    OpenUrl(FamilyBoard.ReleasesUrl(f.App)); Status("Opened " + name + "'s releases page.");
                    return;
                case "start":
                    if (f.App == "Setup") { StartApp("Setup"); return; }
                    if (FamilyRestarter.StartExe(Family.Exe(f.App), f.Arg ?? FamilyTray.StartArgs(f.App))) { Status("Starting " + name + "..."); CheckAgainSoon(5); }
                    else Status(name + " didn't start.");
                    return;
                case "open":
                    if (!Family.Running(f.App)) { StartApp(f.App); Status("Opening " + name + "..."); CheckAgainSoon(8); return; }
                    goto case "pipe";
                case "pipe":
                    bt.IsEnabled = false;
                    new Thread(() =>
                    {
                        var r = Family.Send(f.App, f.Arg);
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            bt.IsEnabled = true;
                            Status(Family.Ok(r) ? (f.Arg == "backup-now" ? "Hallow Vault is backing up the family now." : "Opened " + name + ".") : name + (r == null ? " isn't answering." : " didn't take that."));
                            CheckAgainSoon(f.Arg == "backup-now" ? 30 : 4);
                        }));
                    }) { IsBackground = true }.Start();
                    return;
                case "url":
                    if (f.Arg != null && (f.Arg.StartsWith("https://") || f.Arg.StartsWith("ms-settings:") || f.Arg.StartsWith("windowsdefender:"))) OpenUrl(f.Arg);
                    return;
                case "hub-startup":
                    SetStartWithWindows(true); if (startupBox != null) startupBox.IsChecked = StartsWithWindows();
                    Status("Hallow Hub starts with Windows now."); RunCheckup(false);
                    return;
                case "bring-back":
                    BringSettingsBack(bt);
                    return;
            }
        }

        void BringSettingsBack(Button bt)
        {
            if (checkup == null) return;
            var left = checkup.Leftovers.Where(l => l.Count > 0).ToList(); if (left.Count == 0) return;
            var apps = left.Select(l => l.App).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a).ToList();
            var running = apps.Where(a => a != "Hub" && Family.Running(a)).ToList();
            if (MessageBox.Show(this, "Bring the settings of " + Checkup.Names(apps) + " back from Claude's sandbox?\n\n" +
                (running.Count > 0 ? "- " + Checkup.Names(running) + " quit" + (running.Count == 1 ? "s" : "") + " for a moment (through " + (running.Count == 1 ? "its" : "their") + " own Quit).\n" : "") +
                "- Your folders are backed up first, to \"Hallow data backup\" in your user folder.\n- Only files newer than yours (or missing) are copied; nothing is deleted.\n" +
                (running.Count > 0 ? "- The apps that were running start again in the tray.\n" : "") +
                (apps.Contains("Hub") ? "- Hub's own settings come back the next time Hub starts.\n" : ""),
                "Bring settings back", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            bt.IsEnabled = false; Status("Bringing settings back...");
            var env = CheckupEnv();
            new Thread(() =>
            {
                BringBackResult res = null; string err = null;
                try { res = Checkup.BringBack(env, left); } catch (Exception e) { err = e.Message; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    checkupDone = res != null ? string.Join("  ", res.Lines) + "  Backup: " + res.BackupDir : "Couldn't bring the settings back: " + err;
                    Status(res != null ? "Settings brought back: " + res.Copied + (res.Copied == 1 ? " file" : " files") + (res.Skipped > 0 ? ", " + res.Skipped + " app(s) skipped - see the Family page" : "") + "." : checkupDone);
                    RunCheckup(false);
                }));
            }) { IsBackground = true, Name = "Hallow bring settings back" }.Start();
        }

        // --snap (test family only): the checkup on the Family page, and the list as text
        void SnapCheckup(string dir)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST"))) return;
            checkup = Checkup.Run(CheckupEnv()); checkupHidden = false; checkupDone = null;
            familyBtn.IsChecked = true; ShowCheckup();
            Snap.Save(this, Path.Combine(dir, "family-checkup.png"), 1180, 900);
            var keepMax = familyScroll.MaxHeight; familyScroll.MaxHeight = 3000; Snap.Save(this, Path.Combine(dir, "family-checkup-all.png"), 1180, 1700); familyScroll.MaxHeight = keepMax;
            familyBtn.IsChecked = false;
            var lines = new List<string> { "summary: " + checkup.Summary() + " (" + checkup.Problems + " problems, " + checkup.Warnings + " warnings)" };
            foreach (var it in checkup.Items) lines.Add(it.Area + " [" + (it.Level == 2 ? "problem" : it.Level == 1 ? "warning" : it.Level == 0 ? "ok" : "skipped") + "] " + it.Id + " | " + it.Title + " | " + (it.Detail ?? "") + " | " + string.Join(", ", it.Fixes.Select(x => x.Label + "=" + x.Kind + (x.Arg != null ? ":" + x.Arg : ""))));
            lines.Add("json: " + new JavaScriptSerializer().Serialize(checkup.ToJson()));
            File.WriteAllText(Path.Combine(dir, "checkup.txt"), string.Join("\r\n", lines));
            checkup = null; ShowCheckup();
        }
    }
}
