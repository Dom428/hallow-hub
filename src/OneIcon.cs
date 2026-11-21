// Hallow Hub v2.4 - One Hallow icon: Hub's tray icon hosts the whole family. Other Hallow apps ask Hub "tray-host" and hide
// their own icon while Hub says on; Hub's menu gets a submenu per Hallow app (its items from "menu", Open, Exit) and shows
// their balloons ("notify"). Turn it off in Settings and every app shows its own icon again within a few seconds.
// v2.13: Guard and Assist join the known apps; "tray-host" also says which apps Hub lists ("apps") and that any other Hallow app
// answering "menu" is hosted too ("generic"); each submenu shows the app's own icon.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace HallowHub
{
    public partial class HubWindow
    {
        readonly List<WF.ToolStripItem> familyItems = new List<WF.ToolStripItem>();

        // pipe commands only Hub has (run on the window's thread, 5 s at most)
        public bool QuitRequested;
        public string FamilyCommand(string cmd, string arg)
        {
            // v2.12: open and quit, like every Hallow app (HALLOW-FAMILY.md v2) - quit answers ok first, then exits like the tray's Quit
            if (cmd == "open")
            {
                if (Snap.On) return "ok";
                return OnUi(ShowFromTray) ? "ok" : "error busy";
            }
            if (cmd == "quit")
            {
                QuitRequested = true;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };   // let the "ok" reach the caller first
                    t.Tick += (s, e) => { t.Stop(); Quit(); };
                    t.Start();
                }));
                return "ok";
            }
            if (cmd == "tray-host")
            {
                bool on = false;
                if (!OnUi(() => { on = S.OneIcon && tray != null && tray.Visible; })) return "error busy";
                return "ok " + FamilyTray.HostJson(on);   // v2.13: + the apps it lists and "generic" (Hallow Guard hides its icon only when listed)
            }
            if (cmd == "notify")
            {
                var p = arg.Split('\t');
                if (p.Length < 3 || p[2].Trim().Length == 0) return "error usage: notify <app><tab><title><tab><text>";
                var app = Clip(p[0].Trim(), 20); var title = Clip(p[1].Trim(), 60); var text = Clip(p[2].Trim(), 240);
                if (title.Length == 0) title = "Hallow " + app;
                bool shown = false;
                if (!OnUi(() => { if (tray != null && tray.Visible) { tray.ShowBalloonTip(6000, title, text, WF.ToolTipIcon.None); shown = true; } })) return "error busy";
                return shown ? "ok" : "error Hub's icon isn't showing";
            }
            return null;
        }
        static string Clip(string s, int n) { return s.Length > n ? s.Substring(0, n) : s; }
        bool OnUi(Action a)
        {
            try { var op = Dispatcher.BeginInvoke(a); return op.Wait(TimeSpan.FromSeconds(5)) == DispatcherOperationStatus.Completed; }
            catch { return false; }
        }

        // rebuilt each time the tray menu opens: one submenu per running Hallow app, "Start <App>" for the others
        void UpdateFamilyMenu(WF.ContextMenuStrip menu)
        {
            foreach (var it in familyItems) { menu.Items.Remove(it); it.Dispose(); }
            familyItems.Clear();
            if (!S.OneIcon) return;
            List<FamilyApp> apps; try { apps = FamilyTray.Fetch(); } catch { return; }
            var add = new List<WF.ToolStripItem>();
            foreach (var fa in apps) add.Add(FamilyMenuFor(fa));
            if (Toolkit.Script != null)
            {
                var tk = new WF.ToolStripMenuItem("Open Hallow Toolkit");
                tk.Click += (s, e) => { try { Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + Toolkit.Script + "\"") { UseShellExecute = false, CreateNoWindow = true }); } catch { } };
                add.Add(tk);
            }
            if (add.Count == 0) return;
            add.Add(new WF.ToolStripSeparator());
            int at = Math.Max(0, menu.Items.IndexOf(quitItem));   // just above Hub's own Quit
            foreach (var it in add) { menu.Items.Insert(at++, it); familyItems.Add(it); }
        }

        WF.ToolStripItem FamilyMenuFor(FamilyApp fa)
        {
            var name = "Hallow " + fa.App;
            if (!fa.Running)
            {
                var st = new WF.ToolStripMenuItem("Start " + name) { Image = AppImage(fa.App) };
                st.Click += (s, e) => { try { Process.Start(new ProcessStartInfo(Family.Exe(fa.App), "--tray") { UseShellExecute = true }); } catch (Exception ex) { Status("Couldn't start " + name + ": " + ex.Message); } };
                return st;
            }
            var sub = new WF.ToolStripMenuItem(name) { Image = AppImage(fa.App) };
            var open = new WF.ToolStripMenuItem("Open"); open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
            open.Click += (s, e) => FamilyMenuSend(fa.App, "open");
            sub.DropDownItems.Add(open);
            var items = fa.Answered ? fa.Items : new List<Dictionary<string, object>>();
            if (items.Count > 0) sub.DropDownItems.Add(new WF.ToolStripSeparator());
            foreach (var d in items)
            {
                var id = Family.Str(d, "id"); if (string.IsNullOrEmpty(id)) continue;
                if (id == "-") { sub.DropDownItems.Add(new WF.ToolStripSeparator()); continue; }
                var mi = new WF.ToolStripMenuItem(Family.Str(d, "label") ?? id);
                object v;
                if (d.TryGetValue("checked", out v) && v is bool) mi.Checked = (bool)v;
                if (d.TryGetValue("enabled", out v) && v is bool) mi.Enabled = (bool)v;
                var cmd = "invoke " + id;
                mi.Click += (s, e) => FamilyMenuSend(fa.App, cmd);
                sub.DropDownItems.Add(mi);
            }
            sub.DropDownItems.Add(new WF.ToolStripSeparator());
            var exit = new WF.ToolStripMenuItem("Exit " + name);
            exit.Click += (s, e) => FamilyMenuSend(fa.App, "quit");
            sub.DropDownItems.Add(exit);
            return sub;
        }

        // v2.13: each app's own icon next to its name (from its exe; none when it isn't installed in the usual place)
        static readonly Dictionary<string, System.Drawing.Bitmap> appImages = new Dictionary<string, System.Drawing.Bitmap>(StringComparer.OrdinalIgnoreCase);
        static System.Drawing.Image AppImage(string app)
        {
            System.Drawing.Bitmap b;
            lock (appImages)
            {
                if (!appImages.TryGetValue(app, out b))
                {
                    b = null;
                    try { if (Family.Installed(app)) using (var ic = System.Drawing.Icon.ExtractAssociatedIcon(Family.Exe(app))) using (var big = ic.ToBitmap()) b = new System.Drawing.Bitmap(big, 16, 16); } catch { b = null; }
                    appImages[app] = b;
                }
                return b == null ? null : (System.Drawing.Image)b.Clone();
            }
        }

        void FamilyMenuSend(string app, string line)
        {
            new Thread(() =>
            {
                var r = Family.Send(app, line);
                if (!Family.Ok(r))
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var msg = "Hallow " + app + " didn't take that" + (r != null && r.StartsWith("error ") ? ": " + r.Substring(6) : r == null ? " (it isn't answering)" : "");
                        Status(msg); if (tray != null) tray.ShowBalloonTip(4000, "Hallow Hub", msg, WF.ToolTipIcon.None);
                    }));
            }) { IsBackground = true }.Start();
        }

        // snapshots: what the tray menu would hold, as text
        string FamilyMenuText()
        {
            var sb = new System.Text.StringBuilder();
            Action<WF.ToolStripItem, string> dump = null;
            dump = (it, ind) =>
            {
                var mi = it as WF.ToolStripMenuItem;
                sb.AppendLine(ind + (it is WF.ToolStripSeparator ? "----" : it.Text + (mi != null && mi.Checked ? "  [x]" : "") + (it.Enabled ? "" : "  (disabled)")));
                if (mi != null) foreach (WF.ToolStripItem c in mi.DropDownItems) dump(c, ind + "    ");
            };
            foreach (var fa in FamilyTray.Fetch()) dump(FamilyMenuFor(fa), "");
            return sb.ToString();
        }
    }
}
