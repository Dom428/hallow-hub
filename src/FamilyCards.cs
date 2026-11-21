// Hallow Hub v2.2 - Hallow family on every game page: Saves (Hallow Vault), Clips (Hallow Clips) and Overlay (Hallow Overlay).
// Each card only shows when that app is installed; nothing here can stop Hub working.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace HallowHub
{
    public partial class HubWindow
    {
        Grid familyRow; string familySig; bool compactCards; readonly FamilyServer familyServer = new FamilyServer();

        // the names a game might go by in another app, and its install folders
        List<string> NamesOf(Game g)
        {
            var r = new List<string>();
            foreach (var c in new[] { g }.Concat(g.Alts)) { if (!string.IsNullOrEmpty(c.Name)) r.Add(c.Name); if (!string.IsNullOrEmpty(c.OriginalName)) r.Add(c.OriginalName); }
            return r.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        static string NormDir(string d) { return (d ?? "").Trim().TrimEnd('\\', '/').Replace('/', '\\'); }
        // match by name (case-insensitive), else by install folder
        Dictionary<string, object> Match(List<Dictionary<string, object>> list, Game g)
        {
            var names = NamesOf(g);
            var byName = list.FirstOrDefault(x => names.Any(n => string.Equals(n, Family.Str(x, "name"), StringComparison.OrdinalIgnoreCase)));
            if (byName != null) return byName;
            var dirs = new[] { g }.Concat(g.Alts).Select(c => NormDir(c.InstallDir)).Where(d => d.Length > 0).ToList();
            return list.FirstOrDefault(x => { var d = NormDir(Family.Str(x, "installDir")); return d.Length > 0 && dirs.Any(y => string.Equals(y, d, StringComparison.OrdinalIgnoreCase)); });
        }

        void ShowFamily(bool force)
        {
            if (familyRow == null) return;
            var g = sel;
            bool vi = Family.Installed("Vault"), ci = Family.Installed("Clips"), oi = Family.Installed("Overlay");
            bool vr = vi && Family.Running("Vault"), cr = ci && Family.Running("Clips"), orr = oi && Family.Running("Overlay");
            // v2.14: Guard, Shield and Assist tiles (each only when that app is there)
            GuardWeek gw = null; ShieldInfo shield = null; List<AssistPc> pcs = null;
            try { gw = ReadGuardWeek(); } catch { } try { shield = ReadShield(); } catch { } try { pcs = ReadAssistPcs(); } catch { }
            bool ar = pcs != null && Family.Running("Assist");
            var sig = (g == null ? "" : g.Id) + "|" + vi + vr + ci + cr + oi + orr + ar + "|" + Family.StatusTime("Vault").Ticks + "|" + Family.StatusTime("Clips").Ticks + "|" + Family.StatusTime("Overlay").Ticks +
                "|" + Family.StatusTime("Guard").Ticks + "|" + Family.StatusTime("Shield").Ticks + "|" + Family.StatusTime("Assist").Ticks + "|" + (gw != null) + (shield != null) + (pcs != null);
            if (!force && sig == familySig) return;
            familySig = sig;
            familyRow.Children.Clear(); familyRow.ColumnDefinitions.Clear();
            bool tiles = gw != null || shield != null || pcs != null;
            if (!(g != null && (vi || ci || oi)) && !tiles) { familyRow.Visibility = Visibility.Collapsed; heroBorder.Height = 330; return; }
            if (g == null) { vi = ci = oi = false; }
            familyRow.Visibility = Visibility.Visible; heroBorder.Height = 300;   // a bit shorter, so the library keeps its room
            Dictionary<string, object> vault = vi ? Family.Status("Vault") : null;
            var vg = vault == null ? null : Match(Family.List(vault, "games"), g);
            var aliases = g == null ? new List<string>() : NamesOf(g); var vn = Family.Str(vg, "name"); if (vn != null && !aliases.Contains(vn, StringComparer.OrdinalIgnoreCase)) aliases.Add(vn);
            var cards = new List<Tuple<FrameworkElement, double>>();
            compactCards = (vi ? 1 : 0) + (ci ? 1 : 0) + (oi ? 1 : 0) + (gw != null ? 1 : 0) + (shield != null ? 1 : 0) + (pcs != null ? 1 : 0) > 4;   // v2.14: many cards - app names go to the tooltip
            if (vi) cards.Add(Tuple.Create(SavesCard(g, vault, vg, vr), 1.0));
            if (ci) cards.Add(Tuple.Create(ClipsCard(g, ci ? Family.Status("Clips") : null, aliases, cr), 1.5));
            if (oi) cards.Add(Tuple.Create(OverlayCard(g, Family.Status("Overlay"), aliases, orr), 0.9));
            if (gw != null) try { cards.Add(Tuple.Create(GuardTile(gw), 0.85)); } catch { }
            if (shield != null) try { cards.Add(Tuple.Create(ShieldTile(shield), 0.85)); } catch { }
            if (pcs != null) try { cards.Add(Tuple.Create(AssistTile(pcs), 0.95)); } catch { }
            for (int i = 0; i < cards.Count; i++)
            {
                familyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cards[i].Item2, GridUnitType.Star) });
                var el = cards[i].Item1; el.Margin = new Thickness(i == 0 ? 0 : 6, 0, i == cards.Count - 1 ? 0 : 6, 0);
                Grid.SetColumn(el, i); familyRow.Children.Add(el);
            }
        }

        Border FamilyCard(string icon, string title, string app, out StackPanel body)
        {
            var b = new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(14), Padding = compactCards ? new Thickness(13, 10, 11, 10) : new Thickness(16, 10, 14, 10), BorderBrush = B("#2E2639"), BorderThickness = new Thickness(1), Height = 96 };
            var sp = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 7), Background = Brushes.Transparent, ToolTip = compactCards ? app : null };
            var an = new TextBlock { Text = app, Foreground = (Brush)FindResource("Muted"), FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Visibility = compactCards ? Visibility.Collapsed : Visibility.Visible };
            DockPanel.SetDock(an, Dock.Right); head.Children.Add(an);
            var ic = Icons.Make(icon, 11, B("#A98BE0")); ic.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(ic, Dock.Left); head.Children.Add(ic);
            head.Children.Add(new TextBlock { Text = title, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = B("#A98BE0"), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(head);
            body = new StackPanel(); sp.Children.Add(body);
            b.Child = sp; return b;
        }
        TextBlock Line(string text, bool muted) { return new TextBlock { Text = text, Foreground = muted ? (Brush)FindResource("Muted") : (Brush)FindResource("Text"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 6) }; }
        Button SmallBtn(string text, string tip)
        {
            return new Button { Content = text, Style = (Style)FindResource("Btn"), Padding = new Thickness(10, 4, 11, 4), FontSize = 11.5, Margin = new Thickness(0, 0, 6, 0), ToolTip = tip };
        }
        static void StartApp(string app) { try { Process.Start(new ProcessStartInfo(Family.Exe(app)) { UseShellExecute = true }); } catch { } }

        // send a command off the window's thread; then show what happened and look at the app's status again
        void FamilySend(string app, string line, string okText, string failText)
        {
            new Thread(() =>
            {
                var r = Family.Send(app, line);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Status(Family.Ok(r) ? okText : failText + (r != null && r.StartsWith("error ") ? " (" + r.Substring(6) + ")" : ""));
                    ShowFamily(true);
                    var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; t.Tick += (s, e) => { t.Stop(); ShowFamily(true); }; t.Start();
                }));
            }) { IsBackground = true }.Start();
        }

        // ---- Saves (Hallow Vault): last backup, Back up now, Restore (opens Vault)
        FrameworkElement SavesCard(Game g, Dictionary<string, object> vault, Dictionary<string, object> vg, bool running)
        {
            StackPanel body; var card = FamilyCard("floppy-disk", "SAVES", "Hallow Vault", out body);
            if (vault == null) body.Children.Add(Line(running ? "Hallow Vault is starting up..." : "Start Hallow Vault to see this game's backups.", true));
            else if (vg == null) body.Children.Add(Line("Hallow Vault doesn't back up " + g.Name + " yet.", true));
            else if (Family.Bool(vg, "off")) body.Children.Add(Line("Backups are off for this game in Hallow Vault.", true));
            else
            {
                var last = Family.Time(vg, "lastBackup"); var n = (int)Family.Num(vg, "versions"); var sz = Size((long)Family.Num(vg, "sizeBytes"));
                var t = Line(last == null ? "Not backed up yet" : "Last backup " + AgoLow(last.Value.ToUniversalTime()), false);
                t.ToolTip = last == null ? null : last.Value.ToString("dddd d MMM yyyy, HH:mm");
                if (n > 0 || sz != null) t.Text += "   ·   " + (n == 1 ? "1 version" : n + " versions") + (sz != null ? ", " + sz : "");
                body.Children.Add(t);
            }
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            var name = Family.Str(vg, "name") ?? g.OriginalName ?? g.Name;
            var busy = Family.Bool(vault, "busy");
            var bk = SmallBtn(busy ? "Backing up..." : "Back up now", running ? "Ask Hallow Vault to back up " + name + " now" : "Start Hallow Vault first");
            bk.IsEnabled = running && vg != null && !busy && !Family.Bool(vg, "off");
            bk.Click += (s, e) => { bk.IsEnabled = false; FamilySend("Vault", "backup " + name, "Hallow Vault is backing up " + name + "...", "Hallow Vault couldn't back up " + name); };
            var rs = SmallBtn("Restore...", "Open Hallow Vault to pick a version to restore");
            rs.Click += (s, e) => StartApp("Vault");
            btns.Children.Add(bk); btns.Children.Add(rs);
            body.Children.Add(btns);
            return card;
        }

        // ---- Clips (Hallow Clips): this game's newest clips as thumbnails - click to open, right-click to show in folder
        FrameworkElement ClipsCard(Game g, Dictionary<string, object> clips, List<string> aliases, bool running)
        {
            StackPanel body; var card = FamilyCard("film", "CLIPS", "Hallow Clips", out body);
            if (clips == null) { body.Children.Add(Line(running ? "Hallow Clips is starting up..." : "Start Hallow Clips to see your clips of this game.", true)); return card; }
            var mine = Family.List(clips, "clips").Where(c => aliases.Any(a => string.Equals(a, Family.Str(c, "game"), StringComparison.OrdinalIgnoreCase))).Take(8).ToList();
            if (mine.Count == 0)
            {
                body.Children.Add(Line("No clips of " + g.Name + " yet.", true));
                var folder = Family.Str(clips, "folder");
                if (folder != null && Directory.Exists(folder)) { var fb = SmallBtn("Open folder", "Open your Hallow Clips folder"); fb.HorizontalAlignment = HorizontalAlignment.Left; fb.Click += (s, e) => { try { Process.Start("explorer.exe", "\"" + folder + "\""); } catch { } }; body.Children.Add(fb); }
                return card;
            }
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -1, 0, 0) };
            foreach (var c in mine.Take(4))
            {
                var file = Family.Str(c, "file"); var thumb = Img(Family.Str(c, "thumb"), 200); var when = Family.Time(c, "time"); var dur = Family.Num(c, "durationSec");
                var t = new Border { Width = 92, Height = 52, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, BorderThickness = new Thickness(1), BorderBrush = B("#3A3248"),
                    Background = thumb != null ? (Brush)new ImageBrush(thumb) { Stretch = Stretch.UniformToFill } : B("#2A2333"),
                    ToolTip = (when != null ? when.Value.ToString("ddd d MMM, HH:mm") : Path.GetFileName(file ?? "")) + (dur > 0 ? "  ·  " + (int)Math.Round(dur) + " s" : "") + (Family.Bool(c, "favorite") ? "  ·  favorite" : "") + "\nClick to watch, right-click to show it in its folder" };
                var gi = new Grid();
                if (thumb == null) { var fi = Icons.Make("film", 18, B("#8F88A0")); fi.HorizontalAlignment = HorizontalAlignment.Center; fi.VerticalAlignment = VerticalAlignment.Center; gi.Children.Add(fi); }
                if (dur > 0) gi.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(190, 0x10, 0x0C, 0x16)), CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 0, 4, 1), Margin = new Thickness(0, 0, 4, 4),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Child = new TextBlock { Text = (int)Math.Round(dur) + "s", FontSize = 10, Foreground = Brushes.White } });
                if (Family.Bool(c, "favorite")) { var st = Icons.Make("star", 10, BI("#F0BE5A")); st.HorizontalAlignment = HorizontalAlignment.Left; st.VerticalAlignment = VerticalAlignment.Top; st.Margin = new Thickness(5); gi.Children.Add(st); }
                t.Child = gi;
                var f2 = file;
                t.MouseEnter += (s, e) => t.BorderBrush = B("#F07820"); t.MouseLeave += (s, e) => t.BorderBrush = B("#3A3248");
                t.MouseLeftButtonUp += (s, e) => { if (f2 != null && File.Exists(f2)) OpenUrl(f2); else Status("That clip isn't there any more."); };
                var m = new ContextMenu();
                var open = new MenuItem { Header = "Open", Icon = Icons.Make("play", 13, B("#ECE8F4")) }; open.Click += (s, e) => { if (f2 != null && File.Exists(f2)) OpenUrl(f2); };
                var show = new MenuItem { Header = "Show in folder", Icon = Icons.Make("folder-open", 13, B("#ECE8F4")) };
                show.Click += (s, e) => { try { if (f2 != null && File.Exists(f2)) Process.Start("explorer.exe", "/select,\"" + f2 + "\""); } catch { } };
                m.Items.Add(open); m.Items.Add(show); t.ContextMenu = m;
                strip.Children.Add(t);
            }
            body.Children.Add(strip);
            return card;
        }

        // ---- Overlay (Hallow Overlay): on or off in this game
        FrameworkElement OverlayCard(Game g, Dictionary<string, object> ov, List<string> aliases, bool running)
        {
            StackPanel body; var card = FamilyCard("layer-group", "OVERLAY", "Hallow Overlay", out body);
            var off = new List<string>(); object o;   // a list of names
            if (ov != null && ov.TryGetValue("offGames", out o) && o is System.Collections.ArrayList) foreach (var x in (System.Collections.ArrayList)o) if (x != null) off.Add(x.ToString());
            var offName = off.FirstOrDefault(n => aliases.Any(a => string.Equals(a, n, StringComparison.OrdinalIgnoreCase)));
            var name = offName ?? g.OriginalName ?? g.Name;
            var sw = new CheckBox { Content = new TextBlock { Text = "Show the overlay in this game", TextWrapping = TextWrapping.Wrap }, Style = (Style)FindResource("Switch"), IsChecked = offName == null, Margin = new Thickness(0, -2, 0, 2) };
            sw.IsEnabled = running;
            sw.Click += (s, e) =>
            {
                var on = sw.IsChecked == true;
                FamilySend("Overlay", (on ? "game-on " : "game-off ") + name, on ? "Hallow Overlay shows in " + g.Name + " again." : "Hallow Overlay stays hidden in " + g.Name + ".", "Hallow Overlay didn't take that");
            };
            body.Children.Add(sw);
            body.Children.Add(new TextBlock { Text = running ? "Hallow Overlay remembers this per game." : ov == null ? "Start Hallow Overlay to change this." : "Start Hallow Overlay to change this (it's " + (offName == null ? "on" : "off") + " here).", Foreground = (Brush)FindResource("Muted"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(52, 0, 0, 0) });
            return card;
        }

        // ---- Hub's side of the link: its pipe (ping, status) and %APPDATA%\HallowHub\status.json
        string lastStatus;
        void StartFamilyServer() { if (Snap.On) return; WriteFamilyStatus(true); familyServer.Extra = FamilyCommand; familyServer.Start(); }
        void WriteFamilyStatus(bool running)
        {
            if (Snap.On) return;
            try
            {
                var playing = tracker.Now.Values.Select(r => Main(r.Game).Name).Distinct().ToList();
                var recent = games.Where(x => !S.For(x.Id).Hidden).OrderByDescending(x => LastOf(x) ?? DateTime.MinValue).ThenByDescending(x => Total(x)).Take(8)
                    .Select(x => new Dictionary<string, object> { { "id", x.Id }, { "name", x.Name }, { "playing", IsRunning(x) } }).ToList();
                var js = new JavaScriptSerializer();
                var lastCrash = LastCrashIso();   // v2.5
                var key = js.Serialize(new object[] { running, playing, recent, lastCrash });   // what matters, without the time
                if (key == lastStatus) return;
                var json = js.Serialize(new Dictionary<string, object> { { "app", "Hub" }, { "version", Program.Version }, { "updated", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") }, { "running", running }, { "playing", playing }, { "recent", recent }, { "lastCrash", lastCrash } });
                familyServer.StatusJson = json;
                lastStatus = key;
                Directory.CreateDirectory(HubSettings.Dir);
                var f = Path.Combine(HubSettings.Dir, "status.json"); var tmp = f + ".tmp";
                File.WriteAllText(tmp, json, new System.Text.UTF8Encoding(false));
                if (File.Exists(f)) File.Replace(tmp, f, null); else File.Move(tmp, f);
            }
            catch { }
        }

        // break reminder / daily limit: on Hallow Overlay when it's running (remind), else the tray pop-up
        void Notify(string title, string text)
        {
            Status(title + " - " + text);
            if (Snap.On) return;
            var line = title + " " + text;
            new Thread(() =>
            {
                var r = Family.Send("Overlay", "remind " + line);
                if (!Family.Ok(r)) Dispatcher.BeginInvoke(new Action(() => { if (tray != null) tray.ShowBalloonTip(8000, title, text, WF.ToolTipIcon.None); }));
            }) { IsBackground = true }.Start();
        }
    }
}
