// Hallow Hub v2.14 - a power plan while a game runs: switch to the plan you picked (for this game, or for all games) when the game
// starts, and back to the plan you had when it closes. Only three powercfg commands are ever run, as you (no admin needed):
//   powercfg /list               - the plans on this PC
//   powercfg /getactivescheme    - the plan in use
//   powercfg /setactive <GUID>   - only a GUID that /list just showed
// Plans are never created, copied, deleted or edited. Tests and --snap use FakePowerCfg (in memory), never the real one.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HallowHub
{
    public interface IPowerCfg { string Run(string args); }   // the output, or null when it failed

    public class RealPowerCfg : IPowerCfg
    {
        public string Run(string args)
        {
            if (!PowerPlans.SafeArgs(args)) return null;
            try
            {
                var exe = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "powercfg.exe");
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.Default };
                using (var p = Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEndAsync(); p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } return null; }
                    return p.ExitCode == 0 ? o.Result : null;
                }
            }
            catch { return null; }
        }
    }

    // in-memory plans for tests and --snap: records every command, never touches Windows
    public class FakePowerCfg : IPowerCfg
    {
        public List<string[]> Plans = new List<string[]> {   // guid, name
            new[] { "381b4222-f694-41f0-9685-ff5bb260df2e", "Balanced" },
            new[] { "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "High performance" },
            new[] { "a1841308-3541-4fab-bc81-f71556f20b4a", "Power saver" },
            new[] { "e9a42b02-d5df-448d-aa00-03f14749eb61", "Ultimate Performance" } };
        public string Active = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public readonly List<string> Log = new List<string>();
        public bool Fail;
        public string Run(string args)
        {
            lock (Log) Log.Add(args);
            if (Fail || !PowerPlans.SafeArgs(args)) return null;
            if (args == "/list") return "\r\nExisting Power Schemes (* Active)\r\n-----------------------------------\r\n" + string.Join("\r\n", Plans.Select(p => "Power Scheme GUID: " + p[0] + "  (" + p[1] + ")" + (p[0] == Active ? " *" : ""))) + "\r\n";
            if (args == "/getactivescheme") { var a = Plans.FirstOrDefault(p => p[0] == Active); return a == null ? null : "Power Scheme GUID: " + a[0] + "  (" + a[1] + ")\r\n"; }
            if (args.StartsWith("/setactive ")) { var g = args.Substring(11).Trim(); if (!Plans.Any(p => p[0] == g)) return null; Active = g; return ""; }
            return null;
        }
    }

    public class PowerPlan { public string Guid, Name; public bool Active; }

    public static class PowerPlans
    {
        static IPowerCfg runner;
        public static IPowerCfg Runner
        {
            get { if (runner == null) runner = Snap.On || Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST") != null ? (IPowerCfg)new FakePowerCfg() : new RealPowerCfg(); return runner; }
            set { runner = value; }
        }
        static readonly Regex GuidRx = new Regex(@"\b([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\b");
        static readonly Regex Line = new Regex(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*(?:\((.*)\))?\s*(\*)?\s*$");
        public static bool IsGuid(string s) { return s != null && Regex.IsMatch(s, @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"); }
        // the only commands Hub ever sends to powercfg
        public static bool SafeArgs(string a) { return a == "/list" || a == "/getactivescheme" || (a != null && a.StartsWith("/setactive ") && IsGuid(a.Substring(11).Trim())); }

        // "Power Scheme GUID: <guid>  (<name>) *" - any language: the GUID, the name in brackets, the star for the active one
        public static List<PowerPlan> Parse(string text)
        {
            var r = new List<PowerPlan>();
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var m = Line.Match(raw.TrimEnd('\r')); if (!m.Success) continue;
                var g = m.Groups[1].Value.ToLowerInvariant(); if (r.Any(x => x.Guid == g)) continue;
                var name = m.Groups[2].Success && m.Groups[2].Value.Trim().Length > 0 ? m.Groups[2].Value.Trim() : g;
                r.Add(new PowerPlan { Guid = g, Name = name, Active = m.Groups[3].Success });
            }
            return r;
        }
        public static List<PowerPlan> List() { return Parse(Runner.Run("/list")); }
        public static string ActiveGuid()
        {
            var o = Runner.Run("/getactivescheme"); if (o == null) return null;
            var m = GuidRx.Match(o); return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
        }
        // only a plan that's on this PC right now
        public static bool SetActive(string guid)
        {
            if (!IsGuid(guid)) return false; guid = guid.ToLowerInvariant();
            if (!List().Any(p => p.Guid == guid)) return false;
            return Runner.Run("/setactive " + guid) != null && ActiveGuid() == guid;
        }
        public static string NameOf(string guid, List<PowerPlan> plans)
        {
            var p = (plans ?? new List<PowerPlan>()).FirstOrDefault(x => x.Guid == (guid ?? "").ToLowerInvariant());
            return p != null ? p.Name : null;
        }
    }

    // one game at a time owns the switch: Start when it starts, Stop when it closes. Back = the plan to go back to.
    public class PowerSwitch
    {
        public string For, Back, Set;   // game id, the plan before, the plan Hub switched to
        // which plan this game wants: its own, "none" = never, "" = the all-games choice
        public static string Wanted(GameData d, HubSettings s)
        {
            var mine = d.PowerPlan ?? "";
            if (mine == "none") return null;
            if (mine.Length > 0) return mine;
            return string.IsNullOrEmpty(s.PowerPlanAll) ? null : s.PowerPlanAll;
        }
        // returns what to say ("power plan High performance"), or null when nothing was done
        public string Start(string gameId, string want, out string problem)
        {
            problem = null;
            if (string.IsNullOrEmpty(want) || For != null) return null;
            var plans = PowerPlans.List();
            if (plans.Count == 0) { problem = "couldn't read the power plans"; return null; }
            var p = plans.FirstOrDefault(x => x.Guid == want.ToLowerInvariant());
            if (p == null) { problem = "the power plan picked for it isn't on this PC any more"; return null; }
            var now = PowerPlans.ActiveGuid();
            if (now == p.Guid) return null;   // already on it: nothing to switch back later
            if (!PowerPlans.SetActive(p.Guid)) { problem = "Windows didn't switch the power plan"; return null; }
            For = gameId; Back = now; Set = p.Guid;
            return "power plan " + p.Name;
        }
        // back to the plan from before - unless you picked another plan yourself while playing
        public string Stop(string gameId)
        {
            if (For == null || For != gameId) return null;
            var back = Back; var set = Set; For = null; Back = null; Set = null;
            if (back == null) return null;
            var now = PowerPlans.ActiveGuid();
            if (now != null && now != set) return null;
            var plans = PowerPlans.List();
            var name = PowerPlans.NameOf(back, plans); if (name == null) return null;
            return PowerPlans.SetActive(back) ? "power plan back to " + name : null;
        }
        // saved in settings while switched, so a Hub that was closed mid-game can still switch back next time
        public string Save() { return For == null ? "" : For + "|" + (Back ?? "") + "|" + (Set ?? ""); }
        public static PowerSwitch Load(string s)
        {
            var w = new PowerSwitch(); var p = (s ?? "").Split('|');
            if (p.Length == 3 && p[0].Length > 0 && PowerPlans.IsGuid(p[1]) && PowerPlans.IsGuid(p[2])) { w.For = p[0]; w.Back = p[1]; w.Set = p[2]; }
            return w;
        }
    }
}

namespace HallowHub
{
    public partial class HubWindow
    {
        PowerSwitch power = new PowerSwitch();
        List<PowerPlan> planCache;
        public List<PowerPlan> Plans(bool fresh) { if (planCache == null || fresh) { try { planCache = PowerPlans.List(); } catch { planCache = new List<PowerPlan>(); } } return planCache; }

        List<string> PowerOnStart(Game g, GameData d)
        {
            var said = new List<string>();
            try
            {
                if (sessionFor == g.Id) return said;   // the Hallow Toolkit game session switches the power plan itself
                string problem; var r = power.Start(g.Id, PowerSwitch.Wanted(d, S), out problem);
                if (r != null) { said.Add(r); S.PowerRestore = power.Save(); S.Save(); }
                else if (problem != null) said.Add(problem);
            }
            catch { }
            return said;
        }
        string PowerOnStop(Game g)
        {
            try
            {
                if (power.For != g.Id) return null;
                var r = power.Stop(g.Id); S.PowerRestore = ""; S.Save(); return r;
            }
            catch { return null; }
        }
        // on Quit, and once after starting up (a Hub that was closed mid-game): back to the plan from before, when that game isn't running
        void PowerRestoreNow(bool quitting)
        {
            try
            {
                if (power.For == null) return;
                var g = games.FirstOrDefault(x => x.Id == power.For);
                if (!quitting && g != null && IsRunning(g)) return;
                power.Stop(power.For); S.PowerRestore = ""; S.Save();
            }
            catch { }
        }
        void PowerAfterStart()
        {
            if (Snap.On || string.IsNullOrEmpty(S.PowerRestore)) return;
            power = PowerSwitch.Load(S.PowerRestore);
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            t.Tick += (s, e) => { t.Stop(); PowerRestoreNow(false); };
            t.Start();
        }
        string PowerLine(GameData d)
        {
            var want = PowerSwitch.Wanted(d, S); if (want == null) return null;
            var name = PowerPlans.NameOf(want, Plans(false)) ?? "a power plan that isn't on this PC";
            return "\u26A1  Power plan " + name + " (back after)";
        }
    }
}
