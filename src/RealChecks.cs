// Hallow Hub v2.22 - Real-world checks: the things only your real PC, games, phone and accounts can prove (NordLynx, the leak
// tests, Spotify ads, kill sounds...). Each check turns green BY ITSELF when the app's own status shows it worked (Hub looks
// every 2 minutes, reading status files only), or you mark it with "Worked" / "Didn't work". A pass is remembered (an ad
// mute or a NordLynx connection is over quickly); a failure the app reports later (a leak) turns it red again.
// Kept in %APPDATA%\HallowHub\real-checks.json - ids, states and times only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowHub
{
    public class RealCheck
    {
        public string Id, App, Title, HowTo, TryLabel, TryApp, TryArg;
        public Func<CheckEnv, string> Proof;   // "pass:<what was seen>", "fail:<what was seen>", "seen:<a hint, not proof>" or null
    }

    public class RealMark { public string State = "", At = "", How = "", Seen = ""; }   // State: pass / fail / ""; How: auto / you

    public static class RealChecks
    {
        public const string NordPassId = "eiaeiblijfjekdanodkjadfinkhbfgcd";

        static object Get(Dictionary<string, object> d, params string[] path)
        {
            object o = d;
            foreach (var p in path) { var m = o as Dictionary<string, object>; if (m == null || !m.TryGetValue(p, out o)) return null; }
            return o;
        }
        static bool True(object o) { return o is bool && (bool)o; }
        static string Str(object o) { return o == null ? null : Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture); }
        static double Num(object o) { try { return o == null ? 0 : Convert.ToDouble(o, System.Globalization.CultureInfo.InvariantCulture); } catch { return 0; } }
        static Dictionary<string, object> Json(string file)
        {
            try { return File.Exists(file) ? new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(file, Encoding.UTF8)) : null; } catch { return null; }
        }

        public static readonly List<RealCheck> All = new List<RealCheck>
        {
            new RealCheck { Id = "vpn-nordlynx", App = "VPN", Title = "NordLynx connects",
                HowTo = "In Hallow VPN pick NordLynx, install WireGuard if it asks (one Windows prompt) and connect.",
                TryLabel = "Open NordLynx", TryApp = "VPN", TryArg = "open nordlynx",
                Proof = e => { var s = e.Status("VPN"); if (s == null) return null;
                    if (True(Get(s, "connected")) && Str(Get(s, "protocol")) == "NordLynx") return "pass:connected over NordLynx";
                    return Str(Get(s, "protocolChoice")) == "nordlynx" ? "seen:NordLynx is picked - connect once" : null; } },
            new RealCheck { Id = "vpn-leaks", App = "VPN", Title = "No leaks while connected",
                HowTo = "Connect Hallow VPN - it tests for IP and DNS leaks by itself a few seconds later.",
                TryLabel = "Open VPN", TryApp = "VPN", TryArg = "open connect",
                Proof = e => { var s = e.Status("VPN"); var l = s == null ? null : Str(Get(s, "leakCheck"));
                    return l == "ok" ? "pass:the leak test came back clean" : l == "fail" ? "fail:the leak test found a leak" : l == "warn" ? "seen:the leak test had a warning - open VPN to see it" : null; } },
            new RealCheck { Id = "torrents-drill", App = "Torrents", Title = "Leak drill passes",
                HowTo = "Hallow Torrents > Proof > Run the leak drill. It drops the VPN on purpose and checks nothing gets out.",
                TryLabel = "Open the drill", TryApp = "Torrents", TryArg = "open proof",
                Proof = e => { var d = Json(Path.Combine(e.DataDir("Torrents"), "leak-drill.json")); if (d == null || Str(Get(d, "state")) != "done") return null;
                    return True(Get(d, "pass")) ? "pass:the drill passed" : "fail:the drill found a leak"; } },
            new RealCheck { Id = "torrents-vpn", App = "Torrents", Title = "VPN comes on for torrents",
                HowTo = "In Torrents' settings turn on \"Connect Hallow VPN by itself\", disconnect the VPN, then start a torrent.",
                TryLabel = "Open settings", TryApp = "Torrents", TryArg = "open settings",
                Proof = e => { var s = e.Status("Torrents"); if (s == null) return null;
                    if (Str(Get(s, "vpnAuto", "state")) == "connected") return True(Get(s, "p2pOnly", "serverP2p")) ? "pass:torrents ran through a P2P VPN server" : "pass:torrents ran through the VPN";
                    return null; } },
            new RealCheck { Id = "auth-phone", App = "Authenticator", Title = "Authenticator on your phone",
                HowTo = "Open Hallow Remote's phone link on your Android, add Authenticator to the home screen and let it back up once.",
                TryLabel = "Open Remote", TryApp = "Remote", TryArg = "open",
                Proof = e => { var d = Path.Combine(e.DataDir("Authenticator"), "autobackups");
                    try { if (Directory.Exists(d) && Directory.GetFiles(d, "auto-*.hallowauth").Length > 0) return "pass:your phone backed up to this PC"; } catch { }
                    return null; } },
            new RealCheck { Id = "browser-nordpass", App = "Browser", Title = "NordPass from the Chrome Web Store",
                HowTo = "On NordPass's store page in Hallow Browser click \"Add to Chrome\", then sign in to NordPass in its toolbar button.",
                TryLabel = "Open the store page", TryApp = "Browser", TryArg = "open https://chromewebstore.google.com/detail/" + NordPassId,
                Proof = e => { var s = Json(Path.Combine(e.DataDir("Browser"), "settings.json")); var l = s == null ? null : Get(s, "Extensions") as System.Collections.ArrayList;
                    if (l == null) return null;
                    foreach (var x in l.OfType<Dictionary<string, object>>())
                        if (Str(Get(x, "Id")) == NordPassId && True(Get(x, "Store"))) return True(Get(x, "Enabled")) ? "pass:NordPass is installed and on" : "seen:NordPass is installed but turned off";
                    return null; } },
            new RealCheck { Id = "mixer-ads", App = "Mixer", Title = "Spotify ads get muted",
                HowTo = "Play Spotify Free with \"Mute Spotify ads\" on in Hallow Mixer and wait for an ad.",
                TryLabel = "Open Mixer", TryApp = "Mixer", TryArg = "open",
                Proof = e => { var s = e.Status("Mixer"); if (s == null) return null;
                    var n = Num(Get(s, "adsMutedToday")); return n > 0 ? "pass:" + n + (n == 1 ? " ad" : " ads") + " muted" : True(Get(s, "spotifyAdMuted")) ? "pass:an ad is muted right now" : null; } },
            new RealCheck { Id = "clips-kill", App = "Clips", Title = "A kill sound saves a clip",
                HowTo = "In Hallow Clips teach it your game's kill sound, set highlights to save, then get a kill.",
                TryLabel = "Open Clips", TryApp = "Clips", TryArg = "open",
                Proof = e => { var s = e.Status("Clips"); if (s == null) return null;
                    if (True(Get(s, "lastSave", "auto")) && Str(Get(s, "lastSave", "title")) == "Kill sound") return "pass:a kill saved a clip";
                    var rec = Get(s, "highlights", "recent") as System.Collections.ArrayList;
                    if (rec != null && rec.OfType<Dictionary<string, object>>().Any(r => Str(Get(r, "kind")) == "kill")) return "seen:a kill was heard - set highlights to save a clip";
                    return null; } },
            new RealCheck { Id = "overlay-low", App = "Overlay", Title = "1% low line in a game",
                HowTo = "Turn on the frame graph in Hallow Overlay, play for a minute and look for the dashed 1% low line.",
                TryLabel = "Open Overlay", TryApp = "Overlay", TryArg = "open",
                Proof = e => { var s = e.Status("Overlay"); return s != null && Num(Get(s, "live", "fpsLow")) > 0 ? "seen:the 1% low was measured in a game - did you see the line?" : null; } },
        };

        // what the apps show now, folded into the saved marks; true when something changed
        public static bool Look(CheckEnv env, Dictionary<string, RealMark> marks, DateTime now)
        {
            bool changed = false;
            foreach (var c in All)
            {
                string p = null; try { p = c.Proof(env); } catch { }
                RealMark m; if (!marks.TryGetValue(c.Id, out m)) { m = new RealMark(); marks[c.Id] = m; changed = true; }
                if (p == null) continue;
                var kind = p.Substring(0, p.IndexOf(':')); var what = p.Substring(p.IndexOf(':') + 1);
                if (kind == "seen") { if (m.State == "" && m.Seen != what) { m.Seen = what; changed = true; } continue; }
                if (m.State == kind && m.How == "auto" && m.Seen == what) continue;
                if (m.State == kind && m.How == "you") { if (m.Seen != what) { m.Seen = what; changed = true; } continue; }
                m.State = kind; m.How = "auto"; m.Seen = what; m.At = now.ToString("yyyy-MM-ddTHH:mm:ss"); changed = true;
            }
            return changed;
        }

        public static string File_(CheckEnv env) { return Path.Combine(env.Roaming, "HallowHub", "real-checks.json"); }
        public static Dictionary<string, RealMark> Load(string file)
        {
            var d = new Dictionary<string, RealMark>();
            try
            {
                var raw = Json(file); if (raw == null) return d;
                foreach (var kv in raw)
                {
                    var o = kv.Value as Dictionary<string, object>; if (o == null) continue;
                    d[kv.Key] = new RealMark { State = Str(Get(o, "state")) ?? "", At = Str(Get(o, "at")) ?? "", How = Str(Get(o, "how")) ?? "", Seen = Str(Get(o, "seen")) ?? "" };
                }
            }
            catch { }
            return d;
        }
        public static void Save(string file, Dictionary<string, RealMark> marks)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                var o = marks.ToDictionary(kv => kv.Key, kv => (object)new Dictionary<string, object> { { "state", kv.Value.State }, { "at", kv.Value.At }, { "how", kv.Value.How }, { "seen", kv.Value.Seen } });
                var tmp = file + ".tmp"; File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(o), Encoding.UTF8);
                if (File.Exists(file)) File.Delete(file); File.Move(tmp, file);
            }
            catch { }
        }
        public static Dictionary<string, object> Counts(Dictionary<string, RealMark> marks)
        {
            int pass = All.Count(c => marks.ContainsKey(c.Id) && marks[c.Id].State == "pass"), fail = All.Count(c => marks.ContainsKey(c.Id) && marks[c.Id].State == "fail");
            return new Dictionary<string, object> { { "passed", pass }, { "failed", fail }, { "total", All.Count } };
        }
        // the pipe's "realchecks": ids, states, times, what was seen (no paths, no account data)
        public static Dictionary<string, object> ToJson(Dictionary<string, RealMark> marks)
        {
            var d = Counts(marks);
            d["checks"] = All.Select(c => { RealMark m; marks.TryGetValue(c.Id, out m); m = m ?? new RealMark();
                return (object)new Dictionary<string, object> { { "id", c.Id }, { "app", c.App }, { "title", c.Title }, { "state", m.State == "" ? "not yet" : m.State }, { "how", m.How }, { "at", m.At }, { "seen", m.Seen } }; }).ToList();
            return d;
        }
    }
}
