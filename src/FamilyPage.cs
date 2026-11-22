// Hallow Hub v2.17 - the whole family: a Family page (the people button next to Stats) with a card for every Hallow app -
// installed / running / not installed, its version, a live line from its status.json, and a few quick actions over its pipe.
// Privacy: an app's status.json goes through Safe() first - only the counts and states listed there are ever read, so an email
// subject or sender, a torrent name, a place, an amount or an address can't reach a card even if an app put one in its file.
// Nothing here runs by itself: every action is a click. Nothing pops up (results go to the status line), and actions that would
// make Windows ask (UAC - a NordLynx connect or disconnect in Hallow VPN) are never offered. Install hands off to Hallow Setup
// (or the app's GitHub releases page) - Hub never downloads or runs an installer.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowHub
{
    public class CardAction
    {
        public string Label, Kind, Line, Tip, OkText, FailText;   // Kind: pipe (sends Line), start, install, url (opens Line), places-set
        public bool Enabled = true, IconOnly;
    }
    public class AppCard
    {
        public string App, Title, Icon, Version, State, StateText, Line, Sub, Tip;   // State: running, stopped, missing, active, idle, server, off
        public int LineLevel, SubLevel;   // 0 plain, 1 good (green), 2 look at it (amber)
        public bool Installed, Running;
        public List<CardAction> Actions = new List<CardAction>();
        public string AllText()
        {
            return string.Join(" | ", new[] { Title, Version, StateText, Line, Sub, Tip }.Concat(Actions.SelectMany(a => new[] { a.Label, a.Tip, a.OkText, a.FailText })).Where(s => !string.IsNullOrEmpty(s)));
        }
    }
    public class PlacesHealth { public bool Ok; public string Version, Error; public DateTime At; }

    public static class FamilyBoard
    {
        public static readonly string[] Groups = { "Play", "Protect", "Everyday" };
        public static readonly string[][] Layout = {
            new[] { "Mixer", "Overlay", "Clips", "Vault", "Remote", "Browser" },
            new[] { "Guard", "Shield", "VPN", "Keys", "Assist" },
            new[] { "Tasks", "Torrents", "Email", "Financials", "Home", "Places" } };
        public static IEnumerable<string> AllApps { get { return Layout.SelectMany(x => x); } }
        // Shield is a browser extension (its bridge writes status.json) and Places a server on the NAS: no exe, no pipe
        public static bool WindowsApp(string app) { return app != "Shield" && app != "Places"; }
        static readonly string[] WithSettings = { "Tasks", "VPN", "Torrents", "Financials", "Email", "Home" };   // "open settings" on their pipes
        public static bool HasSettings(string app) { return WithSettings.Contains(app); }
        public static string Icon(string app)
        {
            switch (app)
            {
                case "Mixer": return "sliders"; case "Overlay": return "layer-group"; case "Clips": return "film"; case "Vault": return "floppy-disk";
                case "Remote": return "mobile-screen"; case "Browser": return "globe"; case "Guard": return "shield-halved"; case "Shield": return "eye-slash";
                case "VPN": return "lock"; case "Keys": return "key"; case "Assist": return "hand-holding-heart"; case "Tasks": return "gauge-high";
                case "Torrents": return "magnet"; case "Email": return "envelope"; case "Financials": return "wallet"; case "Home": return "house"; case "Places": return "location-dot";
            }
            return "gamepad";
        }
        public static string Blurb(string app)
        {
            switch (app)
            {
                case "Mixer": return "Per-app volume: game, chat, music"; case "Overlay": return "A stats panel over your games"; case "Clips": return "Save the last moments of a game";
                case "Vault": return "Backs up your game saves"; case "Remote": return "Your phone as a remote"; case "Browser": return "A fast browser for gamers, Shield built in";
                case "Guard": return "A safety center on top of Defender"; case "Shield": return "Blocks ads and trackers in your browser"; case "VPN": return "Your NordVPN, game-aware";
                case "Keys": return "Your passwords, on your PC"; case "Assist": return "Remote help for friends and family"; case "Tasks": return "A better Task Manager";
                case "Torrents": return "Torrents that only run over your VPN"; case "Email": return "A private email client"; case "Financials": return "Private personal finance";
                case "Home": return "Pets and smart home through Home Assistant"; case "Places": return "Family location sharing on your NAS";
            }
            return "";
        }
        public static string ReleasesUrl(string app) { return "https://github.com/Dom428/hallow-" + app.ToLowerInvariant() + "/releases/latest"; }
        public const string PlacesHowTo = "https://github.com/Dom428/hallow-places/blob/main/NAS-DEPLOY.md";

        // ---- the privacy filter: the only fields of each app's status that Hub reads. b = true/false, n = a number (0 or more),
        // v = a version, t = a time, e:a|b = one of these words, r:<regex> = a short word that matches, c = how many in a list,
        // p = a name turned into true/false (a game being played, VPN home mode), accounts / browsers = counts and browser names only.
        static readonly string[] Common = { "version v", "updated t", "lastCrash t" };
        static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
            { "VPN", new[] { "running b", "connected b", "connecting b", "server r:^[a-z]{2}(-[a-z]{2})?[0-9]{1,5}$", "country r:^[A-Za-z][A-Za-z .'()-]{1,39}$", "protocol e:IKEv2|NordLynx", "killSwitch b", "paused b", "game p", "home p", "account b" } },
            { "Torrents", new[] { "running b", "total n", "active n", "seeding n", "paused n", "queued n", "done n", "flagged n", "downKBps n", "upKBps n", "vpnLocked b", "held e:vpn|game", "game p" } },
            { "Browser", new[] { "running b", "tabs n", "asleep n", "windows n", "gameMode b", "shield e:extension|fallback|missing|starting", "downloadsRunning n" } },
            { "Email", new[] { "running b", "unread n", "outbox n", "accounts accounts" } },
            { "Tasks", new[] { "running b", "cpu n", "ram n", "gpu n", "processes n", "sampling b", "game p", "lowered n" } },
            { "Financials", new[] { "running b", "vault b", "locked b" } },
            { "Home", new[] { "running b", "configured b", "connected b", "entities n", "cameras n", "camerasOnline n", "alerts n" } },
            { "Shield", new[] { "blockedToday n", "blockedWeek n", "browsers browsers" } },
            { "Mixer", new string[0] },
            { "Vault", new[] { "running b", "busy b", "games c" } },
            { "Clips", new[] { "recording b", "seconds n", "clips c" } },
            { "Overlay", new[] { "visible b" } },
            { "Remote", new[] { "running b", "listening b", "paused b" } },
            { "Keys", new[] { "running b", "locked b" } },
            { "Guard", new[] { "running b", "score n", "scoreLabel r:^[A-Za-z][A-Za-z ,.'!-]{0,29}$", "threats n" } },
            { "Assist", new[] { "running b", "session e:none|waiting|asking|connected", "helping b" } },
        };
        static readonly string[] ShieldNames = { "Chrome", "Edge", "Brave", "Opera", "Vivaldi", "Chromium", "Firefox" };
        public static IEnumerable<string> AllowedKeys(string app)
        {
            string[] f; if (!Allowed.TryGetValue(app ?? "", out f)) return new string[0];
            var keys = Common.Concat(f).Select(x => x.Substring(0, x.IndexOf(' '))).ToList();
            if (keys.Contains("accounts")) keys.Add("accountErrors");
            return keys;
        }
        public static Dictionary<string, object> Safe(string app, Dictionary<string, object> raw)
        {
            string[] fields; if (raw == null || !Allowed.TryGetValue(app ?? "", out fields)) return null;
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Common.Concat(fields))
            {
                var sp = f.IndexOf(' '); var key = f.Substring(0, sp); var kind = f.Substring(sp + 1);
                object v; if (!raw.TryGetValue(key, out v)) continue;
                try
                {
                    var s = v as string;
                    if (kind == "b") { if (v is bool) d[key] = v; }
                    else if (kind == "n")
                    {
                        double x;
                        if (v == null) d[key] = null;
                        else if (!(v is string) && !(v is bool) && double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out x) && x >= 0 && x < 1e12) d[key] = x;
                    }
                    else if (kind == "v") { if (s != null && Regex.IsMatch(s, @"^\d{1,4}(\.\d{1,5}){0,3}$")) d[key] = s; }
                    else if (kind == "t") { DateTime t; if (s != null && s.Length <= 40 && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out t)) d[key] = t; }
                    else if (kind.StartsWith("e:")) { if (s != null && kind.Substring(2).Split('|').Contains(s)) d[key] = s; }
                    else if (kind.StartsWith("r:")) { if (s != null && s.Length <= 40 && Regex.IsMatch(s, kind.Substring(2))) d[key] = s; }
                    else if (kind == "p") d[key] = v is bool ? (bool)v : v != null && !(s != null && s.Trim().Length == 0);
                    else if (kind == "c") { var a = v as ArrayList; if (a != null) d[key] = (double)a.Count; }
                    else if (kind == "accounts")
                    {
                        var a = v as ArrayList; if (a == null) continue;
                        d["accounts"] = (double)a.Count;
                        d["accountErrors"] = (double)a.Cast<object>().Count(o => { var e = o as Dictionary<string, object>; return e != null && Family.Str(e, "state") == "error"; });
                    }
                    else if (kind == "browsers")
                    {
                        var a = v as ArrayList; if (a == null) continue;
                        var list = new List<Dictionary<string, object>>();
                        foreach (var o in a)
                        {
                            var e = o as Dictionary<string, object>; if (e == null) continue;
                            var n = Family.Str(e, "name"); if (n == null || !ShieldNames.Contains(n)) continue;
                            var b = new Dictionary<string, object> { { "name", n } };
                            var u = Family.Str(e, "updated"); DateTime t;
                            if (u != null && u.Length <= 40 && DateTime.TryParse(u, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out t)) b["updated"] = t;
                            list.Add(b); if (list.Count >= 30) break;
                        }
                        d["browsers"] = list;
                    }
                }
                catch { }
            }
            return d;
        }
        // readers for a Safe() dictionary
        static bool Is(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is bool && (bool)o; }
        static bool Has(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null; }
        static double Num(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is double ? (double)o : 0; }
        static int Int(Dictionary<string, object> d, string k) { return (int)Math.Round(Num(d, k)); }
        static string Str(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o as string : null; }
        static DateTime? When(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is DateTime ? (DateTime)o : (DateTime?)null; }
        public static string Count(int n, string one, string many) { return n.ToString("#,0", CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many); }
        public static string Speed(double kbps) { return kbps < 1000 ? Math.Round(kbps).ToString(CultureInfo.InvariantCulture) + " KB/s" : (kbps / 1024).ToString("0.#", CultureInfo.InvariantCulture) + " MB/s"; }

        public static string ExeVersion(string app)
        {
            try
            {
                var f = Family.Exe(app); if (!File.Exists(f)) return null;
                var v = FileVersionInfo.GetVersionInfo(f).FileVersion; if (string.IsNullOrEmpty(v)) return null;
                var p = v.Split('.').ToList(); while (p.Count > 2 && p[p.Count - 1] == "0") p.RemoveAt(p.Count - 1);
                return string.Join(".", p);
            }
            catch { return null; }
        }

        // what else a card needs to know (read by Describe(app), given straight to the pure Describe in tests)
        public class Extra { public bool SetupThere, ShieldThere, BrowserShield; public string PlacesUrl; public PlacesHealth Places; }

        public static AppCard Describe(string app, string placesUrl, PlacesHealth places)
        {
            bool win = WindowsApp(app), inst = win && Family.Installed(app), run = inst && Family.Running(app);
            var st = win || app == "Shield" ? Safe(app, Family.Status(app)) : null;
            var x = new Extra { SetupThere = Family.Installed("Setup"), PlacesUrl = placesUrl, Places = places };
            if (app == "Shield") { try { x.ShieldThere = HubWindow.ShieldThere(); } catch { } }
            if (app == "Shield" && Family.Installed("Browser") && Family.Running("Browser")) x.BrowserShield = Str(Safe("Browser", Family.Status("Browser")), "shield") == "extension";
            return Describe(app, inst, run, inst ? ExeVersion(app) : null, st, x);
        }

        // the card for one app, from what's known about it (no file or pipe here, so the tests feed it anything)
        public static AppCard Describe(string app, bool installed, bool running, string version, Dictionary<string, object> st, Extra x)
        {
            x = x ?? new Extra();
            var c = new AppCard { App = app, Title = "Hallow " + app, Icon = Icon(app), Installed = installed, Running = running, Version = version ?? Str(st, "version") };
            if (!WindowsApp(app)) { c.Version = app == "Shield" ? Str(st, "version") : null; return app == "Shield" ? ShieldCard(c, st, x) : PlacesCard(c, x); }
            if (!installed)
            {
                c.State = "missing"; c.StateText = "Not installed"; c.Version = null; c.Line = Blurb(app);
                c.Sub = x.SetupThere ? "Install it with Hallow Setup" : "Get it from its GitHub releases page";
                c.Actions.Add(InstallAction(app, x));
                return c;
            }
            var open = new CardAction { Label = "Open", Kind = running ? "pipe" : "start", Line = "open", IconOnly = true, Tip = running ? "Open Hallow " + app : "Start Hallow " + app,
                OkText = "Opened Hallow " + app + ".", FailText = "Hallow " + app + " didn't open" };
            if (!running)
            {
                c.State = "stopped"; c.StateText = app == "Browser" ? "Closed" : "Not running";
                c.Line = app == "Browser" ? "Not open" : "Not running";
                c.Sub = app == "Browser" ? "It runs while its window is open" : "Start it to see how it's doing";
                c.Actions.Add(open);
                return c;
            }
            c.State = "running"; c.StateText = "Running";
            if (st == null) { c.Line = "Starting up..."; c.Sub = "No status from it yet"; }
            else switch (app)
            {
                case "VPN": VpnCard(c, st); break;
                case "Torrents": TorrentsCard(c, st); break;
                case "Browser": BrowserCard(c, st); break;
                case "Email": EmailCard(c, st); break;
                case "Tasks": TasksCard(c, st); break;
                case "Financials": FinancialsCard(c, st); break;
                case "Home": HomeCard(c, st); break;
                default: OlderCard(c, st); break;
            }
            c.Actions.Add(open);
            if (HasSettings(app)) c.Actions.Add(new CardAction { Label = "Settings", Kind = "pipe", Line = "open settings", IconOnly = true, Tip = "Hallow " + app + "'s settings", OkText = "Opened Hallow " + app + "'s settings.", FailText = "Hallow " + app + " didn't open its settings" });
            return c;
        }
        static CardAction InstallAction(string app, Extra x)
        {
            return new CardAction { Label = "Install", Kind = "install", Line = app,
                Tip = x.SetupThere ? "Opens Hallow Setup - tick Hallow " + app + " there and click Install" : "Opens Hallow " + app + "'s releases page on GitHub - Hub downloads nothing itself" };
        }

        static void VpnCard(AppCard c, Dictionary<string, object> st)
        {
            var proto = Str(st, "protocol"); bool nl = proto == "NordLynx";
            bool on = Is(st, "connected"), connecting = Is(st, "connecting");
            if (on)
            {
                var srv = Str(st, "server"); var country = Str(st, "country");
                var num = srv == null ? null : Regex.Match(srv, "[0-9]+$").Value;
                c.Line = "Connected" + (country != null ? " - " + country + (string.IsNullOrEmpty(num) ? "" : " #" + num) : srv != null ? " - " + srv : "") + (proto != null ? " (" + proto + ")" : "");
                c.LineLevel = 1;
            }
            else if (connecting) c.Line = "Connecting...";
            else if (Is(st, "paused")) c.Line = "Paused for your game - reconnects after";
            else if (Is(st, "home")) c.Line = "Home mode (Tailscale exit node)";
            else c.Line = "Not connected";
            c.Sub = "Kill switch " + (Is(st, "killSwitch") ? "on" : "off");
            if (!on && !connecting && Has(st, "account") && !Is(st, "account")) { c.Sub += " - add your Nord account in Hallow VPN"; c.SubLevel = 2; }
            if ((on || connecting) && !nl)
                c.Actions.Add(new CardAction { Label = "Disconnect", Kind = "pipe", Line = "disconnect", Tip = "Disconnect Hallow VPN", OkText = "Hallow VPN is disconnecting.", FailText = "Hallow VPN didn't disconnect" });
            else if (!on && !connecting && !Is(st, "paused") && !Is(st, "home") && Is(st, "account") && proto == "IKEv2")
                c.Actions.Add(new CardAction { Label = "Connect fastest", Kind = "pipe", Line = "connect fastest", Tip = "Connect Hallow VPN to the fastest server (IKEv2 - Windows doesn't ask)", OkText = "Hallow VPN is connecting to the fastest server...", FailText = "Hallow VPN didn't connect" });
            if (nl) c.Tip = "NordLynx asks Windows (UAC) to connect and disconnect, so Hub leaves that to Hallow VPN - click Open.";
        }
        static void TorrentsCard(AppCard c, Dictionary<string, object> st)
        {
            int active = Int(st, "active"), seeding = Int(st, "seeding"), paused = Int(st, "paused"), done = Int(st, "done"), total = Int(st, "total"), flagged = Int(st, "flagged");
            if (Is(st, "vpnLocked") || Str(st, "held") == "vpn") { c.Line = "Locked - VPN off"; c.LineLevel = 2; }
            else if (Str(st, "held") == "game") c.Line = "Paused for your game";
            else if (active > 0) { c.Line = active + " downloading · " + Speed(Num(st, "downKBps")); c.LineLevel = 1; }
            else if (total == 0 && Has(st, "total")) c.Line = "No torrents";
            else c.Line = "Nothing downloading";
            var parts = new List<string>();
            if (seeding > 0) parts.Add(seeding + " seeding"); if (paused > 0) parts.Add(paused + " paused"); if (done > 0) parts.Add(done + " done");
            if (flagged > 0) { parts.Add(flagged + " flagged"); c.SubLevel = 2; }
            c.Sub = parts.Count > 0 ? string.Join(" · ", parts) : Has(st, "total") ? Count(total, "torrent", "torrents") : null;
            c.Actions.Add(new CardAction { Label = "Pause all", Kind = "pipe", Line = "pause-all", Tip = "Pause every torrent", OkText = "Hallow Torrents paused everything.", FailText = "Hallow Torrents didn't pause" });
            c.Actions.Add(new CardAction { Label = "Resume all", Kind = "pipe", Line = "resume-all", Tip = "Resume every torrent (they still only run over the VPN)", OkText = "Hallow Torrents resumed everything.", FailText = "Hallow Torrents didn't resume" });
        }
        static void BrowserCard(AppCard c, Dictionary<string, object> st)
        {
            int tabs = Int(st, "tabs"), asleep = Int(st, "asleep"), win = Int(st, "windows");
            c.Line = Has(st, "tabs") ? Count(tabs, "tab", "tabs") + (asleep > 0 ? " (" + asleep + " asleep)" : "") + (win > 1 ? " in " + win + " windows" : "") : "Open";
            var parts = new List<string>(); var sh = Str(st, "shield");
            if (sh == "extension") parts.Add("Shield built in"); else if (sh == "fallback") parts.Add("Shield: basic blocking"); else if (sh == "missing") { parts.Add("Shield missing"); c.SubLevel = 2; } else if (sh == "starting") parts.Add("Shield starting");
            if (Is(st, "gameMode")) parts.Add("Gaming-light mode");
            int dl = Int(st, "downloadsRunning"); if (dl > 0) parts.Add(Count(dl, "download", "downloads"));
            c.Sub = parts.Count > 0 ? string.Join(" · ", parts) : null;
        }
        static void EmailCard(AppCard c, Dictionary<string, object> st)
        {
            int unread = Int(st, "unread"), acc = Int(st, "accounts"), err = Int(st, "accountErrors"), outbox = Int(st, "outbox");
            c.Line = unread > 0 ? unread.ToString("#,0", CultureInfo.InvariantCulture) + " unread" : "No unread mail";
            var parts = new List<string>();
            if (Has(st, "accounts")) parts.Add(acc == 0 ? "No accounts yet" : Count(acc, "account", "accounts"));
            if (err > 0) { parts.Add(err + (err == 1 ? " needs attention" : " need attention")); c.SubLevel = 2; }
            if (outbox > 0) parts.Add(outbox + " waiting to send");
            c.Sub = parts.Count > 0 ? string.Join(" · ", parts) : null;
        }
        static void TasksCard(AppCard c, Dictionary<string, object> st)
        {
            if (Has(st, "cpu"))
            {
                c.Line = "CPU " + Int(st, "cpu") + "% · RAM " + Int(st, "ram") + "%" + (Has(st, "gpu") ? " · GPU " + Int(st, "gpu") + "%" : "");
                var at = When(st, "updated"); if (at != null && (DateTime.Now - at.Value).TotalMinutes > 3) c.Tip = "As of " + at.Value.ToString("HH:mm") + " - Hallow Tasks samples while its window is open";
            }
            else c.Line = "In the tray - not sampling";
            var parts = new List<string>();
            int pr = Int(st, "processes"); if (pr > 0) parts.Add(Count(pr, "process", "processes"));
            int lo = Int(st, "lowered"); if (lo > 0 && Is(st, "game")) parts.Add(lo + " lowered for your game");
            c.Sub = parts.Count > 0 ? string.Join(" · ", parts) : null;
        }
        static void FinancialsCard(AppCard c, Dictionary<string, object> st)
        {
            if (Has(st, "vault") && !Is(st, "vault")) { c.Line = "No vault yet"; return; }
            bool locked = Is(st, "locked");
            c.Line = locked ? "Vault locked" : Has(st, "locked") ? "Vault unlocked" : "Running";
            c.Sub = "Hub only sees whether it's locked";
            if (Has(st, "locked") && !locked) c.Actions.Add(new CardAction { Label = "Lock now", Kind = "pipe", Line = "lock", Tip = "Lock the Hallow Financials vault now", OkText = "Hallow Financials is locked.", FailText = "Hallow Financials didn't lock" });
        }
        static void HomeCard(AppCard c, Dictionary<string, object> st)
        {
            if (!Is(st, "configured")) { c.Line = "Home Assistant not set up"; c.Sub = "Add its address in Hallow Home's settings"; return; }
            if (Is(st, "connected")) { c.Line = "Home Assistant connected"; c.LineLevel = 1; } else { c.Line = "Home Assistant not reachable"; c.LineLevel = 2; }
            var parts = new List<string>();
            if (Has(st, "entities")) parts.Add(Count(Int(st, "entities"), "device", "devices"));
            if (Int(st, "cameras") > 0) parts.Add(Int(st, "camerasOnline") + "/" + Int(st, "cameras") + " cameras online");
            int al = Int(st, "alerts"); if (al > 0) { parts.Add(Count(al, "alert", "alerts")); c.SubLevel = 2; }
            c.Sub = parts.Count > 0 ? string.Join(" · ", parts) : null;
        }
        static void OlderCard(AppCard c, Dictionary<string, object> st)
        {
            switch (c.App)
            {
                case "Overlay": c.Line = Has(st, "visible") ? (Is(st, "visible") ? "Panel showing" : "Panel hidden") : "Running"; break;
                case "Clips":
                    c.Line = Is(st, "recording") ? "Recording" + (Int(st, "seconds") > 0 ? " - keeps the last " + Int(st, "seconds") + " s" : "") : "Not recording";
                    if (Is(st, "recording")) c.LineLevel = 1;
                    if (Has(st, "clips")) c.Sub = Count(Int(st, "clips"), "clip", "clips") + " saved"; break;
                case "Vault": c.Line = Is(st, "busy") ? "Backing up..." : Has(st, "games") ? Count(Int(st, "games"), "game", "games") + " backed up" : "Running"; break;
                case "Remote": c.Line = Is(st, "paused") ? "Paused" : Has(st, "listening") ? (Is(st, "listening") ? "Your phone can connect" : "Not listening") : "Running"; break;
                case "Keys": c.Line = Has(st, "locked") ? (Is(st, "locked") ? "Locked" : "Unlocked") : "Running"; break;
                case "Guard":
                    if (Has(st, "score")) { var sc = Int(st, "score"); c.Line = "Safety score " + sc + (Str(st, "scoreLabel") != null ? " - " + Str(st, "scoreLabel") : ""); c.LineLevel = sc >= 75 ? 1 : 2; } else c.Line = "No safety score yet";
                    if (Int(st, "threats") > 0) { c.Sub = Count(Int(st, "threats"), "threat", "threats") + " in Defender's history"; c.SubLevel = 2; } break;
                case "Assist":
                    var s = Str(st, "session");
                    c.Line = Is(st, "helping") ? "You're helping someone" : s == "connected" ? "A helper is connected" : s == "asking" ? "Someone is asking to connect" : s == "waiting" ? "Waiting for a helper" : "No session"; break;
                default: c.Line = "Running"; break;
            }
        }
        static AppCard ShieldCard(AppCard c, Dictionary<string, object> st, Extra x)
        {
            c.Installed = x.ShieldThere || st != null;
            if (!c.Installed)
            {
                c.State = "missing"; c.StateText = "Not set up"; c.Line = Blurb("Shield"); c.Sub = "A browser extension - Hallow Setup puts it in place";
                c.Actions.Add(InstallAction("Shield", x)); return c;
            }
            if (st == null)
            {
                c.State = "idle"; c.StateText = "In your browser"; c.Line = "Not sharing counts with Hub"; c.Sub = "Turn it on in Shield: Stats > Share counts";
                c.Tip = HubWindow.ShieldHowTo; return c;
            }
            var recent = new List<string>();
            object bo; var list = st.TryGetValue("browsers", out bo) ? bo as List<Dictionary<string, object>> : null;
            if (list != null) foreach (var b in list) { var u = When(b, "updated"); var n = Str(b, "name"); if (n != null && u != null && (DateTime.Now - u.Value).TotalHours < 24 && !recent.Contains(n)) recent.Add(n); }
            if (x.BrowserShield) recent.Add("Hallow Browser");
            c.State = recent.Count > 0 ? "active" : "idle"; c.StateText = recent.Count > 0 ? "Active" : "Idle";
            c.Line = recent.Count > 0 ? "Active in " + string.Join(", ", recent) : "No browser with Shield today";
            if (recent.Count > 0) c.LineLevel = 1;
            c.Sub = Has(st, "blockedToday") ? Num(st, "blockedToday").ToString("#,0", CultureInfo.InvariantCulture) + " blocked today" + (Has(st, "blockedWeek") ? " · " + Num(st, "blockedWeek").ToString("#,0", CultureInfo.InvariantCulture) + " this week" : "") : "Blocked counts are off in Shield";
            return c;
        }
        static AppCard PlacesCard(AppCard c, Extra x)
        {
            if (string.IsNullOrEmpty(x.PlacesUrl))
            {
                c.State = "off"; c.StateText = "Not set up"; c.Line = "Not set up"; c.Sub = "A server on your NAS - add its address";
                c.Actions.Add(new CardAction { Label = "Set address...", Kind = "places-set", Tip = "Type the address of your Hallow Places server (for example https://places.example.com)" });
                c.Actions.Add(new CardAction { Label = "How to set up", Kind = "url", Line = PlacesHowTo, IconOnly = true, Tip = "How to put Hallow Places on your NAS (opens GitHub)" });
                return c;
            }
            c.Installed = true; c.State = "server"; c.StateText = "Server";
            if (x.Places == null) { c.Line = "Checking the server..."; }
            else if (x.Places.Ok) { c.Line = "Server reachable"; c.LineLevel = 1; c.Version = x.Places.Version; c.State = "active"; c.StateText = "Online"; }
            else { c.Line = "Server not reachable"; c.LineLevel = 2; c.Tip = x.Places.Error; c.StateText = "Offline"; }
            c.Sub = "The map opens in your browser";
            c.Actions.Add(new CardAction { Label = "Open", Kind = "url", Line = x.PlacesUrl, IconOnly = true, Tip = "Open the Hallow Places map in your browser (sign in there)" });
            c.Actions.Add(new CardAction { Label = "Change address...", Kind = "places-set", IconOnly = true, Tip = "Change the address of your Hallow Places server" });
            return c;
        }

        // ---- Hallow Places: only an address you typed, and only its read-only /healthz (no people, no positions)
        public static string PlacesAddress(string typed)
        {
            Uri u; var t = (typed ?? "").Trim();
            if (t.Length == 0 || t.Length > 200 || !Uri.TryCreate(t, UriKind.Absolute, out u)) return null;
            if ((u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp) || !string.IsNullOrEmpty(u.UserInfo) || string.IsNullOrEmpty(u.Host) || !string.IsNullOrEmpty(u.Query) || !string.IsNullOrEmpty(u.Fragment)) return null;
            return u.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }
        public static PlacesHealth CheckPlaces(string address)
        {
            var h = new PlacesHealth { At = DateTime.Now };
            var a = PlacesAddress(address); if (a == null) { h.Error = "That isn't a web address"; return h; }
            try
            {
                var rq = (HttpWebRequest)WebRequest.Create(a + "/healthz");
                rq.Method = "GET"; rq.Timeout = 4000; rq.ReadWriteTimeout = 4000; rq.AllowAutoRedirect = false; rq.UserAgent = "HallowHub/" + Program.Version;
                rq.Accept = "application/json";
                using (var rs = (HttpWebResponse)rq.GetResponse())
                using (var s = rs.GetResponseStream())
                {
                    var buf = new byte[4096]; int n = 0, r;
                    while (n < buf.Length && (r = s.Read(buf, n, buf.Length - n)) > 0) n += r;
                    var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(buf, 0, n));
                    var app = Family.Str(d, "app") ?? "";
                    if (Family.Bool(d, "ok") && app.IndexOf("Places", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        h.Ok = true; var v = Family.Str(d, "version"); if (v != null && Regex.IsMatch(v, @"^\d{1,4}(\.\d{1,5}){0,3}$")) h.Version = v;
                    }
                    else h.Error = "It answered, but not as Hallow Places";
                }
            }
            catch (WebException e) { h.Error = e.Response is HttpWebResponse ? "It answered " + (int)((HttpWebResponse)e.Response).StatusCode : "No answer (" + e.Status + ")"; }
            catch (Exception) { h.Error = "It answered, but not as Hallow Places"; }
            return h;
        }
    }

    public partial class HubWindow
    {
        Border familyPanel; ToggleButton familyBtn; StackPanel familyBody; ScrollViewer familyScroll; DispatcherTimer familyTimer; string familyPageSig;
        PlacesHealth placesHealth; bool placesChecking, placesEditing;

        ToggleButton BuildFamilyButton()
        {
            var b = new ToggleButton { Content = "Family", Tag = "icon", Width = 36, Height = 34, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), ToolTip = "The Hallow family: every app, how it's doing, quick actions" };
            b.Checked += (s, e) =>
            {
                newsPanel.Visibility = Visibility.Collapsed; aboutPanel.Visibility = Visibility.Collapsed; wishPanel.Visibility = Visibility.Collapsed;
                gearBtn.IsChecked = false; statsBtn.IsChecked = false; storageBtn.IsChecked = false;
                placesEditing = false; FillFamily(true); familyPanel.Visibility = Visibility.Visible;
                if (!Snap.On)
                {
                    CheckPlacesSoon(false);
                    if (familyTimer == null) { familyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) }; familyTimer.Tick += (o, a) => { if (familyPanel != null && familyPanel.Visibility == Visibility.Visible) FillFamily(false); else familyTimer.Stop(); }; }
                    familyTimer.Start();
                }
            };
            b.Unchecked += (s, e) => { familyPanel.Visibility = Visibility.Collapsed; placesEditing = false; if (familyTimer != null) familyTimer.Stop(); };
            return b;
        }
        Border BuildFamilyPage()
        {
            var b = new Border { Background = B("#211B2A"), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
                Padding = new Thickness(20, 16, 20, 18), MaxWidth = 1010, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(20, 62, 20, 0), Visibility = Visibility.Collapsed };
            b.Effect = HallowLook.Shadow(30, 0.6, 4);
            var sp = new StackPanel();
            var sh = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var close = new Button { Content = "x", Tag = "icon", Style = (Style)FindResource("Btn"), Width = 30, Height = 28, Padding = new Thickness(0), ToolTip = "Close" };
            close.Click += (s, e) => familyBtn.IsChecked = false;
            DockPanel.SetDock(close, Dock.Right); sh.Children.Add(close);
            var ci = Icons.Make("people-group", 17, B("#F07820")); ci.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(ci, Dock.Left); sh.Children.Add(ci);
            sh.Children.Add(new TextBlock { Text = "The Hallow family", FontWeight = FontWeights.Bold, FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(sh);
            sp.Children.Add(new TextBlock { Text = "Every Hallow app at a glance - live from their status files, counts and states only. Buttons act only when you click them.", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
            familyBody = new StackPanel();
            familyScroll = new ScrollViewer { Content = familyBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, -12, 0), Padding = new Thickness(0, 0, 12, 0) };
            SizeChanged += (s, e) => { if (familyScroll != null) familyScroll.MaxHeight = Math.Max(240, ActualHeight - 170); };
            familyScroll.MaxHeight = Math.Max(240, Height - 170);
            sp.Children.Add(familyScroll);
            b.Child = sp; return b;
        }

        List<AppCard> FamilyCards()
        {
            var list = new List<AppCard>();
            foreach (var a in FamilyBoard.AllApps)
            {
                try { list.Add(FamilyBoard.Describe(a, S.PlacesUrl, placesHealth)); }
                catch { list.Add(new AppCard { App = a, Title = "Hallow " + a, Icon = FamilyBoard.Icon(a), State = "stopped", StateText = "-", Line = "Couldn't read its status" }); }
            }
            return list;
        }
        // the page again, only when something on it changed (it checks every 3 s while it's open)
        void FillFamily(bool force)
        {
            if (familyBody == null || (placesEditing && !force)) return;
            var cards = FamilyCards();
            var sig = string.Join("\n", cards.Select(c => c.State + "|" + c.AllText()));
            if (!force && sig == familyPageSig) return;
            familyPageSig = sig;
            var keep = familyScroll.VerticalOffset;
            familyBody.Children.Clear();
            for (int g = 0; g < FamilyBoard.Groups.Length; g++)
            {
                var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, g == 0 ? 6 : 14, 0, 4) };
                var gi = Icons.Make(g == 0 ? "gamepad" : g == 1 ? "shield-halved" : "house", 11, B("#A98BE0")); gi.Margin = new Thickness(0, 0, 8, 0); head.Children.Add(gi);
                head.Children.Add(new TextBlock { Text = FamilyBoard.Groups[g].ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = B("#A98BE0"), VerticalAlignment = VerticalAlignment.Center });
                familyBody.Children.Add(head);
                var ug = new UniformGrid { Columns = 3, Margin = new Thickness(-5, 0, -5, 0) };
                foreach (var a in FamilyBoard.Layout[g]) { var c = cards.FirstOrDefault(x => x.App == a); if (c != null) ug.Children.Add(CardView(c)); }
                familyBody.Children.Add(ug);
            }
            familyScroll.ScrollToVerticalOffset(keep);
        }

        FrameworkElement CardView(AppCard c)
        {
            bool missing = c.State == "missing" || c.State == "off";
            var b = new Border { Background = (Brush)FindResource("Panel"), CornerRadius = new CornerRadius(12), BorderBrush = B("#2E2639"), BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 11, 12, 10), Margin = new Thickness(5), Height = 128, ToolTip = c.Tip, Tag = c.App };
            var gr = new Grid();
            foreach (var h in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) gr.RowDefinitions.Add(new RowDefinition { Height = h });
            // header: icon, name, version - and the state on the right
            var head = new DockPanel();
            var state = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (c.State == "running" || c.State == "active" || c.State == "stopped" || c.State == "idle" || c.State == "server")
            {
                var on = c.State == "running" || c.State == "active";
                var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                if (on) dot.Fill = BI("#6ED28C"); else { dot.Stroke = B("#8F88A0"); dot.StrokeThickness = 1.2; }
                state.Children.Add(dot);
            }
            state.Children.Add(new TextBlock { Text = c.StateText ?? "", FontSize = 11, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(state, Dock.Right); head.Children.Add(state);
            var ic = Icons.Make(c.Icon, 15, B(missing ? "#8F88A0" : "#F07820")); ic.Margin = new Thickness(0, 0, 9, 0); ic.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(ic, Dock.Left); head.Children.Add(ic);
            var tt = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            tt.Inlines.Add(new System.Windows.Documents.Run(c.Title) { FontWeight = FontWeights.SemiBold, FontSize = 13.5 });
            if (!string.IsNullOrEmpty(c.Version)) tt.Inlines.Add(new System.Windows.Documents.Run("  " + c.Version) { FontSize = 11, Foreground = (Brush)FindResource("Muted") });
            head.Children.Add(tt);
            Grid.SetRow(head, 0); gr.Children.Add(head);
            Func<int, Brush> col = lv => lv == 1 ? (Brush)B("#6ED28C") : lv == 2 ? (Brush)B("#F0BE5A") : null;
            var line = new TextBlock { Text = c.Line ?? "", FontSize = 12.5, Margin = new Thickness(0, 9, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = c.Line };
            line.Foreground = col(c.LineLevel) ?? (missing ? (Brush)FindResource("Muted") : (Brush)FindResource("Text"));
            Grid.SetRow(line, 1); gr.Children.Add(line);
            if (!string.IsNullOrEmpty(c.Sub))
            {
                var sub = new TextBlock { Text = c.Sub, FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = c.Sub, Foreground = col(c.SubLevel) ?? (Brush)FindResource("Muted") };
                Grid.SetRow(sub, 2); gr.Children.Add(sub);
            }
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            if (c.App == "Places" && placesEditing) btns.Children.Add(PlacesEditor());
            else foreach (var a in c.Actions)
            {
                var act = a; var card = c;
                var bt = act.IconOnly
                    ? new Button { Content = act.Label, Tag = "icon", Width = 32, Height = 28, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 6, 0), ToolTip = act.Tip, IsEnabled = act.Enabled }
                    : new Button { Content = act.Label, Style = (Style)FindResource(act.Kind == "install" ? "Accent" : "Btn"), Padding = new Thickness(9, 4, 10, 4), FontSize = 11.5, Margin = new Thickness(0, 0, 6, 0), ToolTip = act.Tip, IsEnabled = act.Enabled };
                bt.Click += (s, e) => RunCardAction(card, act, bt);
                btns.Children.Add(bt);
            }
            Grid.SetRow(btns, 4); gr.Children.Add(btns);
            b.Child = gr;
            return b;
        }

        // a click on a card's button: the pipe command off the window's thread, then the result on the status line (never a pop-up)
        void RunCardAction(AppCard c, CardAction a, Button bt)
        {
            var name = "Hallow " + c.App;
            switch (a.Kind)
            {
                case "pipe":
                    bt.IsEnabled = false;
                    new Thread(() =>
                    {
                        var r = Family.Send(c.App, a.Line);
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            Status(Family.Ok(r) ? a.OkText : (a.FailText ?? name + " didn't take that") + (r == null ? " (it isn't answering)" : r.StartsWith("error ") ? " (" + r.Substring(6) + ")" : ""));
                            bt.IsEnabled = true; FillFamily(false);
                            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) }; t.Tick += (s, e) => { t.Stop(); FillFamily(false); }; t.Start();
                        }));
                    }) { IsBackground = true }.Start();
                    break;
                case "start":
                    StartApp(c.App); Status("Starting " + name + "...");
                    break;
                case "install": InstallApp(c.App); break;
                case "url":
                    if (a.Line != null && (a.Line.StartsWith("https://") || a.Line.StartsWith("http://"))) OpenUrl(a.Line);
                    break;
                case "places-set": placesEditing = true; FillFamily(true); break;
            }
        }
        // Install: Hallow Setup when it's on this PC (tick the app there), else the app's releases page - nothing is downloaded or run by Hub
        void InstallApp(string app)
        {
            if (Family.Installed("Setup"))
            {
                try { Process.Start(new ProcessStartInfo(Family.Exe("Setup")) { UseShellExecute = true }); Status("Hallow Setup is open - tick Hallow " + app + " and click Install."); return; }
                catch { }
            }
            OpenUrl(FamilyBoard.ReleasesUrl(app));
            Status("Opened Hallow " + app + "'s releases page - download the zip there and run INSTALL.bat (or use Hallow Setup).");
        }

        FrameworkElement PlacesEditor()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var tb = new TextBox { Style = (Style)FindResource("Field"), Width = 170, Text = S.PlacesUrl ?? "", VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Your Hallow Places address, e.g. https://places.example.com" };
            var save = new Button { Content = "Save", Style = (Style)FindResource("Btn"), Padding = new Thickness(8, 3, 9, 3), FontSize = 11.5, Margin = new Thickness(6, 0, 0, 0) };
            var cancel = new Button { Content = "x", Tag = "icon", Style = (Style)FindResource("Btn"), Width = 28, Height = 26, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0), ToolTip = "Cancel" };
            save.Click += (s, e) =>
            {
                var t = tb.Text.Trim();
                if (t.Length == 0) { S.PlacesUrl = ""; S.Save(); placesHealth = null; placesEditing = false; FillFamily(true); Status("Hallow Places address removed."); return; }
                var a = FamilyBoard.PlacesAddress(t);
                if (a == null) { Status("That isn't a web address - it starts with https:// (for example https://places.example.com)."); return; }
                S.PlacesUrl = a; S.Save(); placesHealth = null; placesEditing = false; FillFamily(true); CheckPlacesSoon(true);
                Status("Saved - checking Hallow Places at " + a + "...");
            };
            cancel.Click += (s, e) => { placesEditing = false; FillFamily(true); };
            tb.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); else if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; cancel.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); } };
            sp.Children.Add(tb); sp.Children.Add(save); sp.Children.Add(cancel);
            Dispatcher.BeginInvoke(new Action(() => { tb.Focus(); tb.SelectAll(); }), DispatcherPriority.Input);
            return sp;
        }
        // the read-only health check of your Places server: when the page opens (at most once a minute) and after you save an address
        void CheckPlacesSoon(bool now)
        {
            var url = S.PlacesUrl;
            if (Snap.On || string.IsNullOrEmpty(url) || placesChecking) return;
            if (!now && placesHealth != null && (DateTime.Now - placesHealth.At).TotalSeconds < 60) return;
            placesChecking = true;
            new Thread(() =>
            {
                var h = FamilyBoard.CheckPlaces(url);
                Dispatcher.BeginInvoke(new Action(() => { placesChecking = false; if (S.PlacesUrl == url) { placesHealth = h; FillFamily(false); } }));
            }) { IsBackground = true }.Start();
        }

        // snapshots / tests: the page as text
        string FamilyCardsText()
        {
            return string.Join("\r\n", FamilyCards().Select(c => c.App + " [" + c.State + "] " + c.AllText() + "  {" + string.Join(", ", c.Actions.Select(a => a.Label + (a.Kind == "pipe" ? "=" + a.Line : ""))) + "}"));
        }
    }
}
