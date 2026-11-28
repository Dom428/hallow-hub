// Hallow Hub v2.19 - keep the family running after updates. Hub remembers which Hallow apps are running (app, exe, when it last
// saw them). When one that was running disappears and its installed exe changes within a few minutes (a new file version, size
// or write time - it was updated), Hub waits until the new exe has stayed the same for 5 s and starts it again with --tray, once
// more if it exits straight away. Hallow Browser is never reopened (it has no tray): Hub only says it was updated. v2.25: Hallow
// Files (no tray either) is started again WITHOUT --tray - a plain start opens its windows with the tabs it saved when it quit. v2.26: Hallow
// Paint, Notes and Uninstall the same way (Notes reopens its session, Paint the unsaved pictures it kept in recovery). Hallow Net starts with --tray.
// It never starts an app you quit yourself (it went away and the exe didn't change), nor one that wasn't running. Settings >
// "Restart family apps after updates" (on by default) and a per-app opt-out. The note waits while you play.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace HallowHub
{
    [DataContract]
    public class WatchedApp
    {
        [DataMember] public string App = "", Exe = "";
        [DataMember] public string State = "running";      // running; gone (went away, watching its exe for a few minutes); quit (you closed it, or it was handled)
        [DataMember] public string LastSeenRunning = "";   // ISO time, UTC
        [DataMember] public string GoneAt = "";
        [DataMember] public string Version = ""; [DataMember] public long Size; [DataMember] public long Write;   // the exe while it ran
    }
    [DataContract]
    public class RestartState
    {
        [DataMember] public List<WatchedApp> Apps = new List<WatchedApp>();
        [DataMember] public string SavedAt = "";
    }

    public class ExePrint
    {
        public bool Exists, Readable; public string Version = ""; public long Size, Write;
        public static ExePrint Of(string exe)
        {
            var p = new ExePrint();
            try
            {
                var fi = new FileInfo(exe); if (!fi.Exists) return p;
                p.Exists = true; p.Size = fi.Length; p.Write = fi.LastWriteTimeUtc.Ticks;
                try { p.Version = FileVersionInfo.GetVersionInfo(exe).FileVersion ?? ""; } catch { }
                try { using (new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read)) { } p.Readable = true; } catch { }   // fails while an installer still has it open for writing
            }
            catch { }
            return p;
        }
        public bool Same(ExePrint o) { return o != null && Exists == o.Exists && Readable == o.Readable && Size == o.Size && Write == o.Write && Version == o.Version; }
        public bool SameAs(WatchedApp w) { return Exists && Size == w.Size && Write == w.Write && Version == (w.Version ?? ""); }
    }

    // the notes Hub shows, held while a game runs and shown together after it
    public class NoteQueue
    {
        readonly List<string> restarted = new List<string>(), other = new List<string>();
        readonly object gate = new object();
        public void Restarted(string what) { lock (gate) if (!restarted.Contains(what)) restarted.Add(what); }
        public void Add(string line) { lock (gate) if (!other.Contains(line)) other.Add(line); }
        public int Count { get { lock (gate) return restarted.Count + other.Count; } }
        // null while a game runs or when there's nothing to say
        public string Flush(bool gameOn)
        {
            lock (gate)
            {
                if (gameOn || restarted.Count + other.Count == 0) return null;
                var lines = new List<string>();
                if (restarted.Count > 0) lines.Add("Updated and restarted: " + string.Join(", ", restarted));
                lines.AddRange(other);
                restarted.Clear(); other.Clear();
                return string.Join("\n", lines);
            }
        }
    }

    public class FamilyRestarter
    {
        public TimeSpan Window = TimeSpan.FromMinutes(5);          // the exe has to change this soon after the app went away
        public TimeSpan Settle = TimeSpan.FromMinutes(2);          // ...and then stay the same within this much longer
        public TimeSpan StableFor = TimeSpan.FromSeconds(5);       // unchanged this long = the new exe is in place
        public TimeSpan MinAfterGone = TimeSpan.FromSeconds(10);   // an app that restarts itself after its own update gets this long first
        public TimeSpan CheckAfterStart = TimeSpan.FromSeconds(4); // still running after this = it started
        public Func<bool> Enabled = () => true;
        public Func<string, bool> OptedOut = a => false;
        public NoteQueue Notes = new NoteQueue();
        public Action Noted;                                        // a note was queued (Hub shows it unless you're playing)
        public Action<string> Log = s => { };
        public Func<string, string, bool> Starter = StartExe;      // (exe, args) -> started
        public Func<HashSet<string>> Running = RunningExes;
        public readonly string StatePath;
        public List<string> Usual = new List<string>();             // the apps that were running when Hub last stopped (sign-out / shutdown included)
        public static readonly string[] NoteOnly = { "Browser" };   // no tray: never reopened, only "it was updated"
        // v2.25: not one of "the apps you usually run" (Start the apps I usually run, the checkup's Running and Start with Windows):
        // Hallow Browser and Hallow Files are windows you open when you need them, and neither has a start-with-Windows switch
        // (v2.26: Hallow Paint, Hallow Notes and Hallow Uninstall too - FamilyTray.Windowed)
        public static bool NotUsual(string app) { return FamilyTray.IsWindowed(app); }
        // Hub never force-stops a family app: Bring settings back asks it to "quit" over its pipe first (Hallow Display then puts its display changes back)
        static readonly string[] NotWatched = { "Hub", "Setup", "Toolkit" };

        readonly Dictionary<string, WatchedApp> apps = new Dictionary<string, WatchedApp>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, ExePrint> newPrint = new Dictionary<string, ExePrint>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, DateTime> newSince = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        readonly object gate = new object();
        bool dirty; DateTime lastSave = DateTime.MinValue;

        public FamilyRestarter(string statePath) { StatePath = statePath; }

        public static string DefaultStatePath()
        {
            var t = Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST");   // tests never touch Hub's real data
            return string.IsNullOrEmpty(t) ? Path.Combine(HubSettings.Dir, "family-running.json") : Path.Combine(t, "HallowHub-family-running.json");
        }

        // ---- the family on this PC: every "Hallow <App>\Hallow<App>.exe" under Programs (Hub, Setup and Toolkit aside)
        public static List<KeyValuePair<string, string>> Installed()
        {
            var r = new List<KeyValuePair<string, string>>();
            try
            {
                var root = Path.GetDirectoryName(Path.GetDirectoryName(Family.Exe("X")));
                if (!Directory.Exists(root)) return r;
                foreach (var d in Directory.GetDirectories(root, "Hallow *").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var app = Path.GetFileName(d).Substring(7);
                    if (app.Length == 0 || app.Length > 24 || !app.All(char.IsLetterOrDigit) || NotWatched.Contains(app, StringComparer.OrdinalIgnoreCase)) continue;
                    var exe = Family.Exe(app); if (File.Exists(exe)) r.Add(new KeyValuePair<string, string>(app, exe));
                }
            }
            catch { }
            return r;
        }
        static string Key(string path) { try { return Path.GetFullPath(path).ToLowerInvariant(); } catch { return (path ?? "").ToLowerInvariant(); } }
        // full paths (lower case) of the Hallow*.exe processes running now - names and paths only
        public static HashSet<string> RunningExes()
        {
            var r = new HashSet<string>();
            foreach (var p in Process.GetProcesses())
            {
                try { if (p.ProcessName.StartsWith("Hallow", StringComparison.OrdinalIgnoreCase)) { var path = Tracker.PathOf(p.Id); if (path != null) r.Add(Key(path)); } }
                catch { }
                finally { p.Dispose(); }
            }
            return r;
        }
        public static bool StartExe(string exe, string args)
        {
            try { using (Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) })) { } return true; }
            catch { return false; }
        }
        public static string Pretty(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            var p = v.Trim().Split('.').ToList();
            while (p.Count > 2 && p[p.Count - 1] == "0") p.RemoveAt(p.Count - 1);
            return string.Join(".", p);
        }
        public static string Name(string app, string version) { var v = Pretty(version); return "Hallow " + app + (v.Length > 0 ? " " + v : ""); }

        // ---- what Hub knows (for the pipe and the tests)
        public List<WatchedApp> Snapshot() { lock (gate) return apps.Values.Select(w => (WatchedApp)w.MemberwiseCloneSafe()).ToList(); }
        public WatchedApp Get(string app) { lock (gate) { WatchedApp w; return apps.TryGetValue(app, out w) ? w : null; } }

        public void Load()
        {
            lock (gate)
            {
                try
                {
                    if (!File.Exists(StatePath)) return;
                    RestartState st;
                    using (var f = File.OpenRead(StatePath)) st = (RestartState)new DataContractJsonSerializer(typeof(RestartState)).ReadObject(f);
                    if (st == null || st.Apps == null) return;
                    DateTime saved; var fresh = DateTime.TryParse(st.SavedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out saved) && DateTime.UtcNow - saved.ToUniversalTime() < Window;
                    foreach (var w in st.Apps.Where(x => x != null && !string.IsNullOrEmpty(x.App)))
                    {
                        if (w.State == "running" || w.State == "gone") Usual.Add(w.App);
                        // Hub was off only briefly (its own update): carry on watching; after longer, nobody knows who closed what - start nothing
                        if (w.State == "running" || w.State == "gone")
                        {
                            if (!fresh) w.State = "quit";
                            else if (w.State == "running") { w.State = "gone"; w.GoneAt = saved.ToUniversalTime().ToString("o"); }
                        }
                        apps[w.App] = w;
                    }
                }
                catch (Exception ex) { Log("couldn't read " + StatePath + ": " + ex.Message); }
            }
        }
        public void Save(bool force)
        {
            lock (gate)
            {
                if (!force && !dirty && DateTime.UtcNow - lastSave < TimeSpan.FromSeconds(60)) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
                    var st = new RestartState { Apps = apps.Values.ToList(), SavedAt = DateTime.UtcNow.ToString("o") };
                    var tmp = StatePath + ".tmp";
                    using (var f = File.Create(tmp)) new DataContractJsonSerializer(typeof(RestartState)).WriteObject(f, st);
                    if (File.Exists(StatePath)) File.Replace(tmp, StatePath, null); else File.Move(tmp, StatePath);
                    dirty = false; lastSave = DateTime.UtcNow;
                }
                catch { }
            }
        }

        static DateTime When(string iso) { DateTime t; return DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out t) ? t.ToUniversalTime() : DateTime.MinValue; }
        static void Remember(WatchedApp w, ExePrint p) { w.Version = p.Version; w.Size = p.Size; w.Write = p.Write; }

        // every few seconds, off the window's thread
        public void Tick()
        {
            var now = DateTime.UtcNow;
            var installed = Installed();
            HashSet<string> running; try { running = Running(); } catch { return; }
            var todo = new List<KeyValuePair<WatchedApp, ExePrint>>();
            lock (gate)
            {
                foreach (var kv in installed)
                {
                    string app = kv.Key, exe = kv.Value;
                    WatchedApp w; apps.TryGetValue(app, out w);
                    if (running.Contains(Key(exe)))
                    {
                        var p = ExePrint.Of(exe);
                        if (w == null) { w = new WatchedApp { App = app }; apps[app] = w; dirty = true; Log(app + ": running"); }
                        if (w.State != "running") { Log(app + ": running again"); dirty = true; }
                        if (!p.SameAs(w) || w.Exe != exe) dirty = true;
                        w.State = "running"; w.Exe = exe; w.LastSeenRunning = now.ToString("o"); w.GoneAt = ""; Remember(w, p);
                        newPrint.Remove(app); newSince.Remove(app);
                        continue;
                    }
                    if (w == null) continue;   // never seen running: never started
                    if (w.State == "running") { w.State = "gone"; w.GoneAt = now.ToString("o"); dirty = true; Log(app + ": went away - watching its exe"); }
                    if (w.State != "gone") continue;
                    var gone = When(w.GoneAt); var pr = ExePrint.Of(exe);
                    bool changed = !pr.SameAs(w);
                    if (!changed)
                    {
                        newPrint.Remove(app); newSince.Remove(app);
                        if (now - gone > Window) { w.State = "quit"; dirty = true; Log(app + ": closed and not updated - you quit it, leaving it closed"); }
                        continue;
                    }
                    ExePrint last; if (!newPrint.TryGetValue(app, out last) || !last.Same(pr)) { newPrint[app] = pr; newSince[app] = now; }
                    bool stable = pr.Exists && pr.Readable && now - newSince[app] >= StableFor;
                    if (stable && now - gone >= MinAfterGone) { todo.Add(new KeyValuePair<WatchedApp, ExePrint>(w, pr)); continue; }
                    if (now - gone > Window + Settle) { w.State = "quit"; dirty = true; Log(app + ": its exe kept changing - giving up"); }
                }
                foreach (var w in apps.Values.Where(x => x.State != "quit" && !installed.Any(i => string.Equals(i.Key, x.App, StringComparison.OrdinalIgnoreCase))).ToList())
                    if (w.State == "gone" && now - When(w.GoneAt) > Window + Settle) { w.State = "quit"; dirty = true; }
            }
            foreach (var t in todo) Restart(t.Key, t.Value);
            Save(false);
        }

        void Restart(WatchedApp w, ExePrint p)
        {
            var name = Name(w.App, p.Version);
            lock (gate) { w.State = "quit"; w.GoneAt = ""; Remember(w, p); dirty = true; newPrint.Remove(w.App); newSince.Remove(w.App); }
            if (NoteOnly.Contains(w.App, StringComparer.OrdinalIgnoreCase))
            {
                Log(w.App + ": updated - note only");
                if (Enabled() && !OptedOut(w.App)) { Notes.Add("Hallow " + w.App + " was updated - open it when you like"); Fire(); }
                return;
            }
            if (!Enabled()) { Log(w.App + ": updated - restarting is off"); return; }
            if (OptedOut(w.App)) { Log(w.App + ": updated - not restarted (you turned it off for this app)"); return; }
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                if (IsRunning(w.Exe)) { Log(w.App + ": already running again"); MarkRunning(w); return; }
                var args = FamilyTray.StartArgs(w.App);   // v2.25: "" for Hallow Files (windowed - it reopens its saved tabs)
                Log(w.App + ": updated to " + Pretty(p.Version) + " - starting it " + (args.Length > 0 ? "with " + args : "with its window") + (attempt == 2 ? " (again)" : ""));
                if (Starter(w.Exe, args))
                {
                    Thread.Sleep(CheckAfterStart);
                    if (IsRunning(w.Exe)) { MarkRunning(w); Notes.Restarted(name); Fire(); return; }
                    Log(w.App + ": exited right away");
                }
            }
            Notes.Add(name + " was updated but didn't start again - open it from the Hallow icon"); Fire();
        }
        bool IsRunning(string exe) { try { return Running().Contains(Key(exe)); } catch { return false; } }
        void MarkRunning(WatchedApp w) { lock (gate) { w.State = "running"; w.LastSeenRunning = DateTime.UtcNow.ToString("o"); dirty = true; } }
        void Fire() { var n = Noted; if (n != null) try { n(); } catch { } }

        // Family page: "Start the apps I usually run" - the ones running when Hub last stopped that are installed and closed now
        // (not Hallow Browser or Hallow Files: they open a window)
        public List<string> UsualToStart()
        {
            HashSet<string> running; try { running = Running(); } catch { running = new HashSet<string>(); }
            var inst = Installed();
            return Usual.Where(a => !NoteOnly.Contains(a, StringComparer.OrdinalIgnoreCase) && !NotUsual(a))
                .Select(a => inst.FirstOrDefault(i => string.Equals(i.Key, a, StringComparison.OrdinalIgnoreCase)))
                .Where(i => i.Key != null && !running.Contains(Key(i.Value))).Select(i => i.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        public List<string> StartUsual()
        {
            var started = new List<string>();
            foreach (var a in UsualToStart()) if (Starter(Family.Exe(a), FamilyTray.StartArgs(a))) started.Add(a);
            return started;
        }
    }

    static class CloneExt
    {
        public static object MemberwiseCloneSafe(this WatchedApp w)
        {
            return new WatchedApp { App = w.App, Exe = w.Exe, State = w.State, LastSeenRunning = w.LastSeenRunning, GoneAt = w.GoneAt, Version = w.Version, Size = w.Size, Write = w.Write };
        }
    }
}
