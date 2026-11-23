// Hallow Hub v2.19 - keep the family running after updates: the window's side of FamilyRestarter (Restarter.cs). Hub runs on your
// PC as you started it, so the apps it starts again run there too. The note ("Updated and restarted: Hallow Gear 1.3.1") is one
// tray balloon, held while a game runs or a full-screen app is in front, like Hub's other notes.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WF = System.Windows.Forms;

namespace HallowHub
{
    public partial class HubWindow
    {
        FamilyRestarter restarter; Button usualBtn; List<string> usualForSnap;

        void StartRestarter()
        {
            restarter = new FamilyRestarter(FamilyRestarter.DefaultStatePath());
            restarter.Enabled = () => S.RestartAfterUpdates;
            restarter.OptedOut = a => { var l = S.RestartOptOut; lock (l) return l.Contains(a, StringComparer.OrdinalIgnoreCase); };
            restarter.Noted = () => Dispatcher.BeginInvoke(new Action(FlushRestartNotes));
            restarter.Load();
            new Thread(() =>
            {
                while (!exiting)
                {
                    try { restarter.Tick(); } catch { }
                    Thread.Sleep(3000);
                }
            }) { IsBackground = true, Name = "Hallow family restarts", Priority = ThreadPriority.BelowNormal }.Start();
        }

        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        // a game Hub knows is running, or Windows says a full-screen app / game is in front (busy, D3D full screen, presentation)
        bool GameOn()
        {
            if (tracker.Now.Count > 0) return true;
            try { int st; if (SHQueryUserNotificationState(out st) == 0 && (st == 2 || st == 3 || st == 4)) return true; } catch { }
            return false;
        }
        void FlushRestartNotes()
        {
            if (restarter == null || restarter.Notes.Count == 0) return;
            var text = restarter.Notes.Flush(GameOn()); if (text == null) return;
            Status(text.Replace("\n", "  "));
            if (tray != null && tray.Visible) tray.ShowBalloonTip(6000, "Hallow Hub", text, WF.ToolTipIcon.None);
        }

        // Settings: the switch, then a chip per installed app (lit = restarted after its updates)
        void AddRestartSettings(StackPanel sp)
        {
            var c = new CheckBox { Content = new TextBlock { Text = "Restart family apps after updates (an app that was running when it was updated starts again in the tray - never one you closed)", TextWrapping = TextWrapping.Wrap }, IsChecked = S.RestartAfterUpdates, Style = (Style)FindResource("Switch") };
            sp.Children.Add(c);
            var chips = new WrapPanel { Margin = new Thickness(52, 0, 0, 4) };
            var inst = FamilyRestarter.Installed();
            foreach (var kv in inst)
            {
                var app = kv.Key; bool on; lock (S.RestartOptOut) on = !S.RestartOptOut.Contains(app, StringComparer.OrdinalIgnoreCase);
                var t = new ToggleButton { Content = app, IsChecked = on, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 5, 5), Padding = new Thickness(9, 3, 9, 3), FontSize = 11,
                    ToolTip = FamilyRestarter.NoteOnly.Contains(app) ? "Hallow " + app + " is never reopened by itself - Hub only tells you it was updated (off: not even that)" : "Start Hallow " + app + " again after an update (off: leave it closed)" };
                t.Click += (s, e) =>
                {
                    lock (S.RestartOptOut) { S.RestartOptOut.RemoveAll(x => string.Equals(x, app, StringComparison.OrdinalIgnoreCase)); if (t.IsChecked != true) S.RestartOptOut.Add(app); }
                    S.Save(); Status(t.IsChecked == true ? "Hallow " + app + " starts again after its updates." : "Hallow " + app + " stays closed after its updates.");
                };
                chips.Children.Add(t);
            }
            if (inst.Count == 0) chips.Children.Add(new TextBlock { Text = "No other Hallow apps installed yet.", Foreground = (Brush)FindResource("Muted"), FontSize = 11 });
            chips.IsEnabled = S.RestartAfterUpdates;
            c.Click += (s, e) =>
            {
                S.RestartAfterUpdates = c.IsChecked == true; chips.IsEnabled = S.RestartAfterUpdates; S.Save();
                Status(S.RestartAfterUpdates ? "Family apps that were running start again after their updates." : "Family apps stay closed after their updates until you open them.");
            };
            sp.Children.Add(chips);
        }

        // Family page header: "Start the apps I usually run" (the ones running when Hub last stopped)
        Button BuildUsualButton()
        {
            usualBtn = new Button { Content = "Start the apps I usually run", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 4, 12, 4), Visibility = Visibility.Collapsed };
            usualBtn.Click += (s, e) =>
            {
                if (restarter == null) return;
                var started = restarter.StartUsual();
                Status(started.Count == 0 ? "Nothing to start - they're all running." : "Started " + string.Join(", ", started.Select(a => "Hallow " + a)) + " in the tray.");
                UpdateUsualButton();
            };
            return usualBtn;
        }
        void UpdateUsualButton()
        {
            if (usualBtn == null) return;
            List<string> list; try { list = usualForSnap ?? (restarter == null ? new List<string>() : restarter.UsualToStart()); } catch { list = new List<string>(); }
            usualBtn.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            usualBtn.ToolTip = "Running when Hub last stopped, closed now: " + string.Join(", ", list.Select(a => "Hallow " + a)) + "\nThey start in the tray. (Hallow Browser opens a window, so open it yourself.)";
        }

        // pipe "family-watch": what Hub remembers - app, state (running / gone / quit), version, last seen running
        string FamilyWatchJson()
        {
            if (restarter == null) return "{\"on\":false,\"apps\":[]}";
            var items = restarter.Snapshot().OrderBy(w => w.App).Select(w => "{\"app\":\"" + w.App + "\",\"state\":\"" + w.State + "\",\"version\":\"" + FamilyRestarter.Pretty(w.Version) + "\",\"lastSeen\":\"" + w.LastSeenRunning + "\"}");
            return "{\"on\":" + (S.RestartAfterUpdates ? "true" : "false") + ",\"apps\":[" + string.Join(",", items) + "],\"usual\":[" + string.Join(",", restarter.Usual.Select(a => "\"" + a + "\"")) + "]}";
        }
    }
}
