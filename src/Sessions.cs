// Hallow Hub v1.6 - session helpers: apps that open with a game, a break reminder, a daily limit per game, and session history.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WF = System.Windows.Forms;

namespace HallowHub
{
    public partial class HubWindow
    {
        // apps Hub opened for a game (game id -> process ids), so only those get closed afterwards
        readonly Dictionary<string, List<int>> companions = new Dictionary<string, List<int>>();
        readonly Dictionary<string, int> breaksTold = new Dictionary<string, int>();

        static string AppName(string exe)
        {
            try { var n = FileVersionInfo.GetVersionInfo(exe).FileDescription; if (!string.IsNullOrWhiteSpace(n)) return n.Trim(); } catch { }
            return Path.GetFileNameWithoutExtension(exe);
        }

        void OpenCompanions(Game g)
        {
            var d = S.For(g.Id); var started = new List<int>(); var names = new List<string>();
            foreach (var exe in d.OpenWith.Where(File.Exists))
            {
                var pn = Path.GetFileNameWithoutExtension(exe);
                if (Process.GetProcessesByName(pn).Length > 0) continue;   // already running - leave it alone, and don't close it later
                try
                {
                    var p = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe), WindowStyle = ProcessWindowStyle.Minimized });
                    if (p != null) started.Add(p.Id);
                    names.Add(AppName(exe));
                }
                catch { }
            }
            if (started.Count > 0) companions[g.Id] = started;
            if (names.Count > 0) Status("Opened " + string.Join(", ", names) + " for " + g.Name + ".");
        }

        void CloseCompanions(Game g)
        {
            List<int> ids; if (!companions.TryGetValue(g.Id, out ids)) return;
            companions.Remove(g.Id);
            if (S.For(g.Id).KeepOpen) return;
            foreach (var id in ids)
            {
                // ask it to close like clicking X (it can still ask to save); never force-killed
                try { var p = Process.GetProcessById(id); if (!p.HasExited) p.CloseMainWindow(); } catch { }
            }
        }

        void LogSession(Game g, DateTime since)
        {
            var mins = (DateTime.Now - since).TotalMinutes; if (mins < 1) return;
            var log = S.For(g.Id).SessionLog;
            log.Add(since.ToString("o") + "|" + mins.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            while (log.Count > 40) log.RemoveAt(0);
        }

        // every poll: break reminder and daily limit
        void CheckReminders()
        {
            foreach (var r in tracker.Now.Values.ToList())
            {
                var g = Main(r.Game); var d = S.For(g.Id);
                var mins = (DateTime.Now - r.Since).TotalMinutes;
                if (S.BreakMinutes > 0)
                {
                    int step = (int)(mins / S.BreakMinutes); int told; breaksTold.TryGetValue(g.Id + "|" + r.Since.Ticks, out told);
                    if (step >= 1 && step > told)
                    {
                        breaksTold[g.Id + "|" + r.Since.Ticks] = step;
                        Notify("Time for a break?", "You've been playing " + g.Name + " for " + HubSettings.Duration(mins) + ". Stretch, drink some water, rest your eyes.");
                    }
                }
                if (d.DailyLimit > 0)
                {
                    var today = d.MinutesSince(DateTime.Today); var key = DateTime.Today.ToString("yyyy-MM-dd");
                    if (today >= d.DailyLimit && d.LimitTold != key)
                    {
                        d.LimitTold = key; S.Save();
                        Notify("Daily limit reached", HubSettings.Duration(today) + " of " + g.Name + " today - that's the limit you set. Finish your match and call it a day?");
                    }
                }
            }
        }
        void Notify(string title, string text)
        {
            Status(title + " - " + text);
            if (tray != null) tray.ShowBalloonTip(8000, title, text, WF.ToolTipIcon.None);
        }

        // for the Stats page: the latest sessions across every game
        FrameworkElement RecentSessions()
        {
            var sp = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            sp.Children.Add(SectionLabel("RECENT SESSIONS", "clock-rotate-left"));
            var all = new List<Tuple<Game, DateTime, double>>();
            foreach (var g in games)
                foreach (var e in S.For(g.Id).SessionLog)
                {
                    var p = e.Split('|'); DateTime t; double m;
                    if (p.Length == 2 && DateTime.TryParse(p[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out t) && double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out m))
                        all.Add(Tuple.Create(g, t, m));
                }
            foreach (var x in all.OrderByDescending(x => x.Item2).Take(8))
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var len = new TextBlock { Text = HubSettings.Duration(x.Item3), Foreground = (Brush)FindResource("Muted"), Width = 80, TextAlignment = TextAlignment.Right };
                DockPanel.SetDock(len, Dock.Right); row.Children.Add(len);
                var when = new TextBlock { Text = x.Item2.ToLocalTime().ToString("ddd d MMM, HH:mm"), Foreground = (Brush)FindResource("Muted"), Width = 150 };
                DockPanel.SetDock(when, Dock.Left); row.Children.Add(when);
                row.Children.Add(new TextBlock { Text = x.Item1.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                sp.Children.Add(row);
            }
            if (all.Count == 0) sp.Children.Add(new TextBlock { Text = "Each game session shows up here once you've played with Hallow Hub running.", Foreground = (Brush)FindResource("Muted") });
            return sp;
        }
    }
}
