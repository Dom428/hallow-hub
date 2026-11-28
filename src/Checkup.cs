// Hallow Hub v2.21 - Family checkup: one click (and a quiet weekly look) that checks the whole Hallow family on this PC:
// versions against GitHub (through Hallow Setup 1.18's --list --check), the apps you usually run, settings stuck in Claude's
// sandbox (%LOCALAPPDATA%\Packages\Claude_*\LocalCache), settings that must be there (Hallow VPN's account.dat - exists only,
// never read; Keys' vault; Gear running), Windows notifications, backups (Vault's family backup, Keys' safety checklist, the
// Authenticator's last backup on the PC), Ransomware protection blocks (Hallow Guard 1.10's cfaBlocked) and Start with Windows.
// Each check is OK / look at this / problem, with a one-click fix where it's safe and the steps otherwise.
// Counts and states only: no file contents are read (Keys' vault and VPN's account.dat are only checked to exist, and copied
// as opaque files by "Bring settings back").
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace HallowHub
{
    public class CheckFix
    {
        public string Label, Kind, App, Arg, Tip;   // Kind: update, start, pipe, open (pipe when running, else start it), url, hub-startup, bring-back
        public CheckFix(string label, string kind, string app, string arg, string tip) { Label = label; Kind = kind; App = app; Arg = arg; Tip = tip; }
    }
    public class CheckItem
    {
        public string Id, Area, Title, Detail;
        public int Level;   // 0 OK, 1 look at this, 2 problem, -1 skipped (not used here / can't tell)
        public List<CheckFix> Fixes = new List<CheckFix>();
        public CheckItem(string id, string area, int level, string title, string detail) { Id = id; Area = area; Level = level; Title = title; Detail = detail; }
        public CheckItem Fix(string label, string kind, string app, string arg, string tip) { Fixes.Add(new CheckFix(label, kind, app, arg, tip)); return this; }
    }
    public class CheckupResult
    {
        public DateTime At; public List<CheckItem> Items = new List<CheckItem>(); public List<Leftover> Leftovers = new List<Leftover>();
        public int Problems { get { return Items.Count(i => i.Level == 2); } }
        public int Warnings { get { return Items.Count(i => i.Level == 1); } }
        public string Summary()
        {
            if (Problems == 0 && Warnings == 0) return "All good";
            var p = new List<string>();
            if (Problems > 0) p.Add(Problems + (Problems == 1 ? " problem" : " problems"));
            if (Warnings > 0) p.Add(Warnings + " to look at");
            return string.Join(", ", p);
        }
        // the pipe's "checkup" answer and status.json's "checkup": counts, ids, levels and the plain titles (app names and counts - no paths)
        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object> { { "lastRun", At.ToString("yyyy-MM-ddTHH:mm:ss") }, { "problems", Problems }, { "warnings", Warnings },
                { "items", Items.Select(i => (object)new Dictionary<string, object> { { "id", i.Id }, { "area", i.Area }, { "level", i.Level == 2 ? "problem" : i.Level == 1 ? "warning" : i.Level == 0 ? "ok" : "skipped" }, { "title", i.Title } }).ToList() } };
        }
    }
    // Hallow data in Claude's sandbox: one Hallow* folder under a Claude_* package's LocalCache\Roaming or LocalCache\Local
    public class Leftover
    {
        public string Name, App, Root, Box, Real;   // Root: Roaming or Local
        public int Newer, Missing, Older;           // files newer than the real copy, missing from it, and older / the same
        public List<string> ToCopy = new List<string>();   // relative paths (never shown - counts only)
        public int Count { get { return Newer + Missing; } }
    }
    public class BringBackResult
    {
        public string BackupDir; public List<string> Lines = new List<string>(); public int Copied, Skipped; public bool HubPending;
    }

    // Where the checkup looks and how it acts - the real PC by default, a fake family in tests (every part can be swapped)
    public class CheckEnv
    {
        public string Roaming, Local, Packages, Profile, Documents;
        public Func<List<KeyValuePair<string, string>>> Installed = FamilyRestarter.Installed;   // (app, exe) - not Hub, Setup, Toolkit
        public Func<string, string> Exe = Family.Exe;
        public Func<string, string> VersionOf = exe => { try { return FileVersionInfo.GetVersionInfo(exe).FileVersion ?? ""; } catch { return ""; } };
        public Func<string> ReleaseJson;                 // Hallow Setup's --list --check --json, or null
        public Func<string, bool> Running = Family.Running;   // the app's pipe is there
        public Func<HashSet<string>> RunningExes = FamilyRestarter.RunningExes;
        public Func<List<string>> Usual = () => new List<string>();
        public Func<string, bool?> StartsWithWindows;    // null = can't tell
        public Func<bool> InPackage = () => false;        // v2.21: is Hub itself running inside an app package (Claude's sandbox)?
        public Func<List<string>> NotificationsOff;      // apps whose notifications Windows has off ("*" = all), null = can't read
        public Func<string, string, string> Send = Family.Send;
        public Func<string, string, bool> Start = FamilyRestarter.StartExe;
        public Func<DateTime> Now = () => DateTime.Now;
        public Action<int> Sleep = ms => Thread.Sleep(ms);
        public int QuitWaitMs = 20000;
        public string HubVersion = Program.Version;
        public string HubPendingFile;                    // where "Bring settings back" leaves Hub's own files for its next start

        public string DataDir(string app) { return Path.Combine(app == "Clips" ? Local : Roaming, "Hallow" + app); }
        public Dictionary<string, object> Status(string app)
        {
            try
            {
                var f = Path.Combine(DataDir(app), "status.json"); if (!File.Exists(f)) return null;
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(f, Encoding.UTF8));
            }
            catch { return null; }
        }
        public bool IsRunning(string app)
        {
            try { if (Running(app)) return true; } catch { }
            try { var exe = Exe(app); return RunningExes().Contains(Path.GetFullPath(exe).ToLowerInvariant()); } catch { return false; }
        }

        // the real PC - or, with HALLOW_FAMILY_TEST, the test family folder (status files, Programs, a fake Packages folder,
        // and small text files for what would come from the registry or Hallow Setup)
        public static CheckEnv Default()
        {
            var t = Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST");
            var e = new CheckEnv();
            if (!string.IsNullOrEmpty(t))
            {
                e.Roaming = t; e.Local = t; e.Packages = Path.Combine(t, "Packages"); e.Profile = Path.Combine(t, "profile"); e.Documents = Path.Combine(t, "profile", "Documents");
                e.ReleaseJson = () => ReadOr(Path.Combine(t, "checkup-list.json"));
                e.StartsWithWindows = app => { var l = Lines(Path.Combine(t, "checkup-startup.txt")); return l == null ? (bool?)null : l.Contains(app, StringComparer.OrdinalIgnoreCase); };
                e.NotificationsOff = () => Lines(Path.Combine(t, "checkup-notify-off.txt"));
                e.Usual = () => Lines(Path.Combine(t, "checkup-usual.txt")) ?? new List<string>();
                e.HubPendingFile = Path.Combine(t, "HallowHub-bring-back.json");
                e.Start = (exe, args) => false;   // tests and snapshots never start anything from here
                e.InPackage = () => File.Exists(Path.Combine(t, "checkup-in-package.txt"));
                return e;
            }
            e.Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            e.Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            e.Packages = Path.Combine(e.Local, "Packages");
            e.Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            e.Documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            e.ReleaseJson = SetupList;
            e.StartsWithWindows = RunKeyOn;
            e.NotificationsOff = WindowsNotificationsOff;
            e.HubPendingFile = Path.Combine(HubSettings.Dir, "bring-back.json");
            e.InPackage = InAppPackage;
            return e;
        }
        // a process started from Claude's (MSIX) app runs with its package identity: GetCurrentPackageFullName then doesn't say "no package" (15700)
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetCurrentPackageFullName(ref int length, StringBuilder name);
        // ...or, without package identity, its AppData writes are redirected: a tiny probe file in Hub's own folder shows up under a
        // Claude_* package's LocalCache (both removed again at once). Worked out once per run of Hub; never in tests or snapshots.
        static bool? boxedCache;
        public static bool InAppPackage()
        {
            if (boxedCache != null) return boxedCache.Value;
            bool boxed = false;
            try { int len = 0; var rc = GetCurrentPackageFullName(ref len, null); boxed = rc != 15700; }   // 122 (buffer too small) or 0 = packaged
            catch { }
            if (!boxed && !Snap.On && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST")))
            {
                string probe = null; var hits = new List<string>();
                try
                {
                    var pk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
                    var pkgs = Directory.Exists(pk) ? Directory.GetDirectories(pk, "Claude_*") : new string[0];
                    if (pkgs.Length > 0)
                    {
                        var name = ".hallow-probe-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        Directory.CreateDirectory(HubSettings.Dir); probe = Path.Combine(HubSettings.Dir, name); File.WriteAllText(probe, "probe");
                        foreach (var d in pkgs) { var h = Path.Combine(d, "LocalCache", "Roaming", "HallowHub", name); if (File.Exists(h)) hits.Add(h); }
                        boxed = hits.Count > 0;
                    }
                }
                catch { }
                finally { try { if (probe != null && File.Exists(probe)) File.Delete(probe); } catch { } foreach (var h in hits) try { if (File.Exists(h)) File.Delete(h); } catch { } }
            }
            boxedCache = boxed; return boxed;
        }
        static string ReadOr(string f) { try { return File.Exists(f) ? File.ReadAllText(f) : null; } catch { return null; } }
        static List<string> Lines(string f) { try { return File.Exists(f) ? File.ReadAllLines(f).Select(x => x.Trim()).Where(x => x.Length > 0).ToList() : null; } catch { return null; } }

        // Hallow Setup 1.18+: "--list --check --json --out <file>" (no window), at most 90 s, cached for 10 minutes
        static string setupCache; static DateTime setupAt;
        public static string SetupList()
        {
            if (setupCache != null && (DateTime.Now - setupAt).TotalMinutes < 10) return setupCache;
            var exe = Family.Exe("Setup"); if (!File.Exists(exe)) return null;
            var tmp = Path.Combine(Path.GetTempPath(), "hallow-hub-checkup-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(exe, "--list --check --json --out \"" + tmp + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, WorkingDirectory = Path.GetDirectoryName(exe) }))
                {
                    try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                    p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(90000)) { try { p.Kill(); } catch { } return null; }
                }
                if (!File.Exists(tmp)) return null;
                var s = File.ReadAllText(tmp, Encoding.UTF8); setupCache = s; setupAt = DateTime.Now; return s;
            }
            catch { return null; }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }
        // HKCU Run "Hallow<App>" - and not switched off in Task Manager's Startup apps (StartupApproved, odd first byte = off)
        public static bool? RunKeyOn(string app)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (k == null || k.GetValue("Hallow" + app) == null) return false;
                }
                using (var a = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"))
                {
                    var b = a == null ? null : a.GetValue("Hallow" + app) as byte[];
                    if (b != null && b.Length > 0 && (b[0] & 1) == 1) return false;
                }
                return true;
            }
            catch { return null; }
        }
        // Windows' per-app notification switches (read only): keys under Notifications\Settings with "Hallow" in the name and Enabled = 0;
        // "*" when all app notifications are off. Apps Windows has no key for are allowed (the default).
        public static List<string> WindowsNotificationsOff()
        {
            try
            {
                var r = new List<string>();
                using (var p = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications"))
                { var v = p == null ? null : p.GetValue("ToastEnabled"); if (v is int && (int)v == 0) r.Add("*"); }
                using (var s = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings"))
                {
                    if (s == null) return r;
                    foreach (var n in s.GetSubKeyNames())
                    {
                        if (n.IndexOf("Hallow", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        using (var k = s.OpenSubKey(n)) { var v = k == null ? null : k.GetValue("Enabled"); if (v is int && (int)v == 0) r.Add(Checkup.AppFromKey(n)); }
                    }
                }
                return r;
            }
            catch { return null; }
        }
    }

    public static class Checkup
    {
        public static readonly string[] Areas = { "Versions", "Running", "Sandbox", "Settings", "Notifications", "Backups", "Ransomware protection", "Start with Windows" };
        // runtime files that are never worth bringing back (the app writes them again as it starts)
        static bool Skip(string rel)
        {
            var n = Path.GetFileName(rel);
            return n.Equals("status.json", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith("crashes" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        public static string AppFromKey(string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(key ?? "", "Hallow[ ._]?([A-Za-z]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? Char.ToUpperInvariant(m.Groups[1].Value[0]) + m.Groups[1].Value.Substring(1) : key;
        }

        // "1.2.10" vs "1.2.9": numbers, not text; missing parts and trailing zeros count as 0 ("1.3" == "1.3.0.0"). Bad input sorts lowest.
        public static int CompareVersions(string a, string b)
        {
            var x = Parts(a); var y = Parts(b);
            if (x == null || y == null) return x == null && y == null ? 0 : x == null ? -1 : 1;
            for (int i = 0; i < Math.Max(x.Count, y.Count); i++)
            {
                var p = i < x.Count ? x[i] : 0; var q = i < y.Count ? y[i] : 0;
                if (p != q) return p < q ? -1 : 1;
            }
            return 0;
        }
        static List<long> Parts(string v)
        {
            v = (v ?? "").Trim().TrimStart('v', 'V'); if (v.Length == 0) return null;
            var r = new List<long>();
            foreach (var s in v.Split('.')) { long n; if (!long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n)) return null; r.Add(n); }
            return r;
        }
        static string Ago(DateTime t, DateTime now)
        {
            var d = now - t;
            if (d.TotalMinutes < 2) return "just now";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + " min ago";
            if (d.TotalHours < 36) return (int)Math.Round(d.TotalHours) + (Math.Round(d.TotalHours) == 1 ? " hour ago" : " hours ago");
            return (int)Math.Round(d.TotalDays) + " days ago";
        }
        // the weekly run: on, 10 minutes after Hub started, no game, and the last one 7 days ago (or never)
        public static bool Due(bool weekly, string lastIso, DateTime now, double uptimeMinutes, bool gameOn)
        {
            if (!weekly || gameOn || uptimeMinutes < 10) return false;
            DateTime last;
            return !DateTime.TryParse(lastIso, null, DateTimeStyles.RoundtripKind, out last) || (now - last).TotalDays >= 7;
        }
        public static string Names(IEnumerable<string> apps) { var l = apps.Select(a => "Hallow " + a).ToList(); return l.Count <= 2 ? string.Join(" and ", l) : string.Join(", ", l.Take(l.Count - 1)) + " and " + l.Last(); }
        static bool Has(List<KeyValuePair<string, string>> inst, string app) { return inst.Any(i => string.Equals(i.Key, app, StringComparison.OrdinalIgnoreCase)); }

        public static CheckupResult Run(CheckEnv env)
        {
            var r = new CheckupResult { At = env.Now() };
            List<KeyValuePair<string, string>> inst; try { inst = env.Installed() ?? new List<KeyValuePair<string, string>>(); } catch { inst = new List<KeyValuePair<string, string>>(); }
            Action<Func<IEnumerable<CheckItem>>, string> safe = (f, area) =>
            {
                try { r.Items.AddRange(f()); }
                catch (Exception e) { r.Items.Add(new CheckItem(area.ToLowerInvariant().Replace(' ', '-') + "-error", area, -1, "Couldn't check this", e.GetType().Name)); }
            };
            safe(() => Versions(env, inst), "Versions");
            safe(() => RunningCheck(env, inst), "Running");
            List<Leftover> left = null;
            safe(() => { left = FindLeftovers(env); r.Leftovers = left; bool boxed = false; try { boxed = env.InPackage(); } catch { } return boxed ? HubBoxed() : SandboxCheck(left); }, "Sandbox");
            safe(() => SettingsCheck(env, inst, left ?? new List<Leftover>()), "Settings");
            safe(() => NotificationsCheck(env, inst), "Notifications");
            safe(() => BackupsCheck(env, inst), "Backups");
            safe(() => RansomwareCheck(env, inst), "Ransomware protection");
            safe(() => StartupCheck(env, inst), "Start with Windows");
            return r;
        }

        // ---- 1. versions: Hallow Setup's list (installed vs the newest release on GitHub)
        public static List<CheckItem> Versions(CheckEnv env, List<KeyValuePair<string, string>> inst)
        {
            var r = new List<CheckItem>();
            string js = null; try { js = env.ReleaseJson == null ? null : env.ReleaseJson(); } catch { }
            List<Dictionary<string, object>> items = null;
            if (js != null) try { items = Family.List(new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(js), "items"); } catch { items = null; }
            if (items == null)
            {
                bool setup = File.Exists(env.Exe("Setup"));
                r.Add(new CheckItem("versions", "Versions", -1, "Couldn't check for updates", setup ? "Hallow Setup didn't answer - it needs version 1.18 or newer (and the internet)." : "Hallow Setup checks the versions - install it to see updates here.")
                    .Fix(setup ? "Open Hallow Setup" : "Get Hallow Setup", setup ? "start" : "url", "Setup", setup ? "" : "https://github.com/Dom428/hallow-setup/releases/latest", null));
                return r;
            }
            var outdated = new List<string>(); int checkedN = 0;
            foreach (var i in items)
            {
                var name = Family.Str(i, "name"); var kind = Family.Str(i, "kind");
                if (name == null || kind != "app" || !Family.Bool(i, "installed")) continue;
                var ver = Family.Str(i, "version"); var latest = Family.Str(i, "latest");
                if (string.Equals(name, "Hub", StringComparison.OrdinalIgnoreCase) && CompareVersions(env.HubVersion, ver) > 0) ver = env.HubVersion;
                checkedN++;
                if (latest == null) continue;
                if (CompareVersions(latest, ver) > 0)
                {
                    outdated.Add(name);
                    r.Add(new CheckItem("update-" + name.ToLowerInvariant(), "Versions", 1, "Update available: Hallow " + name + " " + latest, "You have " + (ver ?? "an older version") + ".")
                        .Fix("Update", "update", name, latest, "Hallow Setup downloads it from GitHub, checks it and installs it"));
                }
            }
            if (outdated.Count == 0) r.Add(new CheckItem("versions", "Versions", 0, checkedN == 0 ? "No Hallow apps to check" : "All " + checkedN + " Hallow apps are up to date", null));
            return r;
        }

        // ---- 2. the apps you usually run (Hub 2.19's list) that aren't running now
        public static List<CheckItem> RunningCheck(CheckEnv env, List<KeyValuePair<string, string>> inst)
        {
            var usual = (env.Usual() ?? new List<string>()).Where(a => Has(inst, a) && !FamilyRestarter.NoteOnly.Contains(a, StringComparer.OrdinalIgnoreCase) && !FamilyRestarter.NotUsual(a) && !string.Equals(a, "Gear", StringComparison.OrdinalIgnoreCase))   // v2.25: not Files (a window you open when you need it)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a).ToList();
            var r = new List<CheckItem>();
            if (usual.Count == 0) { r.Add(new CheckItem("running", "Running", -1, "Hub hasn't learned which apps you usually run yet", "It remembers the Hallow apps that are running when it stops.")); return r; }
            var closed = usual.Where(a => !env.IsRunning(a)).ToList();
            foreach (var a in closed)
                r.Add(new CheckItem("running-" + a.ToLowerInvariant(), "Running", 1, "Hallow " + a + " isn't running", "You usually have it running.").Fix("Start", "start", a, "--tray", "Start Hallow " + a + " in the tray"));
            if (closed.Count == 0) r.Add(new CheckItem("running", "Running", 0, usual.Count == 1 ? "Hallow " + usual[0] + " is running, as usual" : "All " + usual.Count + " apps you usually run are running", null));
            return r;
        }

        // ---- 3. Hallow data in Claude's sandbox that's newer than the real copy, or missing from it
        // v2.24: Local folders that hold only a cache the app rebuilds by itself - never "brought back" from the sandbox (Hallow Find's
        // index.bin lists every file name the sandboxed copy could see; its settings in Roaming\HallowFind are still checked)
        static readonly string[] LocalCacheOnly = { "HallowFind" };
        public static List<Leftover> FindLeftovers(CheckEnv env)
        {
            var r = new List<Leftover>();
            if (string.IsNullOrEmpty(env.Packages) || !Directory.Exists(env.Packages)) return r;
            foreach (var pkg in Directory.GetDirectories(env.Packages, "Claude_*"))
            {
                foreach (var root in new[] { "Roaming", "Local" })
                {
                    var box = Path.Combine(pkg, "LocalCache", root); if (!Directory.Exists(box)) continue;
                    foreach (var d in Directory.GetDirectories(box, "Hallow*"))
                    {
                        var name = Path.GetFileName(d);
                        if (name.StartsWith("HallowBrowser-backup-", StringComparison.OrdinalIgnoreCase) || name.Equals("HallowDev", StringComparison.OrdinalIgnoreCase) || name.Equals("Hallow", StringComparison.OrdinalIgnoreCase)) continue;
                        if (root == "Local" && LocalCacheOnly.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;   // v2.24: a rebuildable cache, not settings
                        var lo = new Leftover { Name = name, App = name.Substring(6), Root = root, Box = d, Real = Path.Combine(root == "Roaming" ? env.Roaming : env.Local, name) };
                        Compare(lo);
                        r.Add(lo);
                    }
                }
            }
            return r;
        }
        static void Compare(Leftover lo)
        {
            foreach (var f in Directory.GetFiles(lo.Box, "*", SearchOption.AllDirectories))
            {
                var rel = f.Substring(lo.Box.Length).TrimStart(Path.DirectorySeparatorChar);
                if (Skip(rel)) continue;
                var real = Path.Combine(lo.Real, rel);
                if (!File.Exists(real)) { lo.Missing++; lo.ToCopy.Add(rel); continue; }
                if (File.GetLastWriteTimeUtc(f) > File.GetLastWriteTimeUtc(real).AddSeconds(2)) { lo.Newer++; lo.ToCopy.Add(rel); }
                else lo.Older++;
            }
        }
        // Hub itself was started from inside Claude's sandbox: what it copies would land in the sandbox again, so it only says so
        public static List<CheckItem> HubBoxed()
        {
            return new List<CheckItem> { new CheckItem("sandbox-hub", "Sandbox", 2, "Hallow Hub itself is running inside Claude's sandbox",
                "Its settings (and any app it starts) go to Claude's folder. Quit Hub (Hallow icon > Quit), open it from the Start menu, and run the checkup again.") };
        }
        public static List<CheckItem> SandboxCheck(List<Leftover> left)
        {
            var r = new List<CheckItem>();
            var stuck = left.Where(l => l.Count > 0).ToList();
            if (stuck.Count > 0)
            {
                var apps = stuck.Select(l => l.App).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a).ToList();
                int files = stuck.Sum(l => l.Count);
                r.Add(new CheckItem("sandbox", "Sandbox", 2, "Settings stuck in Claude's sandbox: " + Names(apps),
                    "These apps once ran inside the Claude app, so Windows kept what they saved in Claude's own folder instead of yours (" + files + (files == 1 ? " file" : " files") + " newer than yours or missing from it). Hub runs outside the sandbox, so it can bring them back: it quits those apps, backs up your folders to \"Hallow data backup\" in your user folder, copies the newer files only, and starts the apps again.")
                    .Fix("Bring settings back", "bring-back", null, null, "Quit " + Names(apps) + ", back up your folders to %USERPROFILE%\\Hallow data backup, copy newer files only, start them again"));
            }
            else if (left.Count > 0) r.Add(new CheckItem("sandbox", "Sandbox", 0, "Nothing stuck in Claude's sandbox", left.Count + " old " + (left.Count == 1 ? "copy is" : "copies are") + " there, none newer than yours."));
            else r.Add(new CheckItem("sandbox", "Sandbox", 0, "Nothing stuck in Claude's sandbox", null));
            return r;
        }

        // Bring settings back - the same steps as Hallow-fix-app-data.ps1, from Hub (outside the sandbox): per app, quit it through
        // its pipe, back up the real folder to %USERPROFILE%\Hallow data backup\<date> (never Documents: Ransomware protection
        // blocks it), copy the sandbox's newer / missing files only (opaque copies - nothing is opened or read), start it again
        // if it was running. An app that won't quit, or whose backup fails, is left exactly as it is. Hub's own folder is copied
        // at Hub's next start (it can't swap its own settings while it runs).
        public static BringBackResult BringBack(CheckEnv env, List<Leftover> left)
        {
            var res = new BringBackResult();
            bool boxed = false; try { boxed = env.InPackage(); } catch { }
            if (boxed) throw new InvalidOperationException("Hub is running inside an app package - its copies would land in the sandbox again");
            var now = env.Now();
            var root = Path.Combine(env.Profile, "Hallow data backup");
            if (!string.IsNullOrEmpty(env.Documents) && Under(root, env.Documents)) throw new InvalidOperationException("the backup would go to Documents");
            var dir = Path.Combine(root, now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture));
            for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(root, now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture) + " (" + n + ")");
            res.BackupDir = dir;
            foreach (var g in left.Where(l => l.Count > 0).GroupBy(l => l.App, StringComparer.OrdinalIgnoreCase))
            {
                var app = g.Key; var name = "Hallow " + app;
                bool isHub = string.Equals(app, "Hub", StringComparison.OrdinalIgnoreCase);
                bool was = !isHub && env.IsRunning(app);
                if (was)
                {
                    string q = null; try { q = env.Send(app, "quit"); } catch { }
                    int waited = 0;
                    while (env.IsRunning(app) && waited < env.QuitWaitMs) { env.Sleep(250); waited += 250; }
                    if (env.IsRunning(app)) { res.Skipped++; res.Lines.Add(name + ": still open" + (Family.Ok(q) ? "" : " (it didn't answer \"quit\")") + " - nothing changed. Quit it from the Hallow icon and try again."); continue; }
                }
                bool ok = true;
                foreach (var lo in g)
                {
                    try { if (Directory.Exists(lo.Real)) CopyTree(lo.Real, Path.Combine(dir, lo.Root, lo.Name), null); }
                    catch (Exception e) { ok = false; res.Lines.Add(name + ": couldn't back up your folder (" + e.GetType().Name + ") - nothing changed."); }
                }
                if (!ok) { res.Skipped++; if (was) Restart(env, app); continue; }
                if (isHub)
                {
                    try
                    {
                        var pend = new Dictionary<string, object> { { "made", now.ToString("o") }, { "backup", dir }, { "folders", g.Select(lo => (object)new Dictionary<string, object> { { "box", lo.Box }, { "real", lo.Real }, { "files", lo.ToCopy.Cast<object>().ToList() } }).ToList() } };
                        Directory.CreateDirectory(Path.GetDirectoryName(env.HubPendingFile));
                        File.WriteAllText(env.HubPendingFile, new JavaScriptSerializer().Serialize(pend), new UTF8Encoding(false));
                        res.HubPending = true; res.Lines.Add("Hallow Hub: backed up; its own settings come back the next time Hub starts (quit Hub and open it again).");
                    }
                    catch (Exception e) { res.Skipped++; res.Lines.Add("Hallow Hub: couldn't note its own files (" + e.GetType().Name + ")."); }
                    continue;
                }
                int copied = 0, failed = 0;
                foreach (var lo in g)
                    foreach (var rel in lo.ToCopy)
                    {
                        try
                        {
                            var src = Path.Combine(lo.Box, rel); var dst = Path.Combine(lo.Real, rel);
                            if (File.Exists(dst) && File.GetLastWriteTimeUtc(dst) >= File.GetLastWriteTimeUtc(src)) continue;   // newer only, checked again now
                            Directory.CreateDirectory(Path.GetDirectoryName(dst));
                            File.Copy(src, dst, true); File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src)); copied++;
                        }
                        catch { failed++; }
                    }
                res.Copied += copied;
                res.Lines.Add(name + ": " + copied + (copied == 1 ? " file" : " files") + " brought back" + (failed > 0 ? ", " + failed + " couldn't be copied" : "") + (was ? " - started again" : "") + ".");
                if (was) Restart(env, app);
            }
            return res;
        }
        static void Restart(CheckEnv env, string app) { try { env.Start(env.Exe(app), FamilyTray.StartArgs(app)); } catch { } }
        static bool Under(string path, string folder)
        {
            try { var p = Path.GetFullPath(path).TrimEnd('\\') + "\\"; var f = Path.GetFullPath(folder).TrimEnd('\\') + "\\"; return p.StartsWith(f, StringComparison.OrdinalIgnoreCase); } catch { return false; }
        }
        // every file, as bytes (nothing is opened as data); skip = relative paths to leave out
        static void CopyTree(string from, string to, Func<string, bool> skip)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var rel = f.Substring(from.Length).TrimStart(Path.DirectorySeparatorChar);
                if (skip != null && skip(rel)) continue;
                var d = Path.Combine(to, rel); Directory.CreateDirectory(Path.GetDirectoryName(d)); File.Copy(f, d, true);
            }
        }
        // Hub's next start: its own files noted by Bring settings back (backed up again first, then copied as they were)
        public static string ApplyHubPending(string pendingFile)
        {
            try
            {
                if (pendingFile == null || !File.Exists(pendingFile)) return null;
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(pendingFile));
                File.Delete(pendingFile);   // once, whatever happens
                var backup = Family.Str(d, "backup"); int n = 0;
                foreach (var f in Family.List(d, "folders"))
                {
                    var box = Family.Str(f, "box"); var real = Family.Str(f, "real");
                    if (box == null || real == null || !Directory.Exists(box)) continue;
                    if (backup != null && Directory.Exists(real)) CopyTree(real, Path.Combine(backup, "Roaming", Path.GetFileName(real) + " (at start)"), null);
                    object o; if (!f.TryGetValue("files", out o) || !(o is System.Collections.ArrayList)) continue;
                    foreach (var x in (System.Collections.ArrayList)o)
                    {
                        var rel = x as string; if (rel == null || rel.Contains("..") || Path.IsPathRooted(rel)) continue;
                        var src = Path.Combine(box, rel); var dst = Path.Combine(real, rel); if (!File.Exists(src)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(dst)); File.Copy(src, dst, true); n++;
                    }
                }
                return n + (n == 1 ? " file" : " files");
            }
            catch { return null; }
        }

        // ---- 4. settings that must be there
        public static List<CheckItem> SettingsCheck(CheckEnv env, List<KeyValuePair<string, string>> inst, List<Leftover> left)
        {
            var r = new List<CheckItem>();
            Func<string, string, bool> inBox = (app, file) => left.Any(l => string.Equals(l.App, app, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(l.Box, file)));
            if (Has(inst, "VPN"))
            {
                if (File.Exists(Path.Combine(env.DataDir("VPN"), "account.dat"))) r.Add(new CheckItem("vpn-account", "Settings", 0, "Hallow VPN has your Nord service credentials", null));
                else r.Add(new CheckItem("vpn-account", "Settings", 2, "Hallow VPN needs your Nord service credentials again",
                    inBox("VPN", "account.dat") ? "A copy is in Claude's sandbox - Bring settings back first." : "Hallow VPN > Account: paste the service username and password from your Nord account page (not your Nord login).")
                    .Fix("Open its Account page", "open", "VPN", "open account", null));
            }
            if (Has(inst, "Keys"))
            {
                if (File.Exists(Path.Combine(env.DataDir("Keys"), "vault.hkvault"))) r.Add(new CheckItem("keys-vault", "Settings", 0, "Your Hallow Keys vault is on this PC", null));
                else r.Add(new CheckItem("keys-vault", "Settings", inBox("Keys", "vault.hkvault") ? 2 : 1, "No Hallow Keys vault on this PC",
                    inBox("Keys", "vault.hkvault") ? "One is in Claude's sandbox - Bring settings back first." : "Open Hallow Keys to create one, or restore your backup.").Fix("Open Hallow Keys", "open", "Keys", "open", null));
            }
            if (Has(inst, "Gear"))
            {
                if (env.IsRunning("Gear")) r.Add(new CheckItem("gear-running", "Settings", 0, "Hallow Gear is watching your batteries", null));
                else r.Add(new CheckItem("gear-running", "Settings", 2, "Hallow Gear isn't running - no low-battery alerts", null).Fix("Start", "start", "Gear", "--tray", "Start Hallow Gear in the tray"));
            }
            if (r.Count == 0) r.Add(new CheckItem("settings", "Settings", -1, "Nothing to check (Hallow VPN, Keys and Gear aren't installed)", null));
            return r;
        }

        // ---- 5. Windows notifications for the family apps (read-only; skipped when Windows' setting can't be read)
        public static List<CheckItem> NotificationsCheck(CheckEnv env, List<KeyValuePair<string, string>> inst)
        {
            List<string> off = null; try { off = env.NotificationsOff == null ? null : env.NotificationsOff(); } catch { }
            if (off == null) return new List<CheckItem> { new CheckItem("notifications", "Notifications", -1, "Couldn't read Windows' notification settings", null) };
            if (off.Contains("*")) return new List<CheckItem> { new CheckItem("notifications", "Notifications", 2, "Windows has notifications off for every app", "Hallow alerts (low batteries, backups, scans) can't show.")
                .Fix("Open notification settings", "url", null, "ms-settings:notifications", "Settings > System > Notifications") };
            var mine = off.Where(a => a == "Hub" || Has(inst, a)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a).ToList();
            if (mine.Count == 0) return new List<CheckItem> { new CheckItem("notifications", "Notifications", 0, "Notifications are allowed for the Hallow apps", null) };
            return new List<CheckItem> { new CheckItem("notifications", "Notifications", 1, "Notifications are off for " + Names(mine), "Turn them on in Settings > System > Notifications.")
                .Fix("Open notification settings", "url", null, "ms-settings:notifications", "Settings > System > Notifications") };
        }

        // ---- 6. backups: Vault's family backup, Keys' safety checklist, the Authenticator's last backup on this PC
        public static List<CheckItem> BackupsCheck(CheckEnv env, List<KeyValuePair<string, string>> inst)
        {
            var r = new List<CheckItem>(); var now = env.Now();
            if (Has(inst, "Vault"))
            {
                var st = env.Status("Vault"); object fo = null;
                var fam = st != null && st.TryGetValue("family", out fo) ? fo as Dictionary<string, object> : null;
                if (fam == null) r.Add(new CheckItem("vault-family", "Backups", -1, "Hallow Vault doesn't report a family backup", "Hallow Vault 1.16 or newer backs up the Hallow apps' settings."));
                else if (!Family.Bool(fam, "on")) r.Add(new CheckItem("vault-family", "Backups", 1, "The Hallow family backup has no destination", "Pick a folder (your NAS or another drive) in Hallow Vault > Hallow family backup.").Fix("Set it up", "open", "Vault", "open family", null));
                else
                {
                    var last = Family.Time(fam, "lastBackup"); var dests = Family.List(fam, "destinations");
                    int errs = dests.Count(d => Family.Str(d, "lastError") != null && !Family.Bool(d, "blocked"));   // blocked ones are check 7's
                    if (dests.Count == 0 && fam.ContainsKey("errors") && fam["errors"] is System.Collections.ArrayList) errs = ((System.Collections.ArrayList)fam["errors"]).Count;
                    if (errs > 0) r.Add(new CheckItem("vault-family", "Backups", 2, "The Hallow family backup failed" + (errs > 1 ? " for " + errs + " destinations" : ""), "Open Hallow Vault's family backup to see why.").Fix("Open it", "open", "Vault", "open family", null));
                    else if (last == null) r.Add(new CheckItem("vault-family", "Backups", 1, "The Hallow family backup hasn't run yet", null).Fix("Back up now", "pipe", "Vault", "backup-now", "Hallow Vault backs up the Hallow apps' settings now"));
                    else if ((now - last.Value).TotalDays > 8 || Family.Bool(fam, "stale")) r.Add(new CheckItem("vault-family", "Backups", 1, "The last Hallow family backup was " + Ago(last.Value, now), null).Fix("Back up now", "pipe", "Vault", "backup-now", "Hallow Vault backs up the Hallow apps' settings now"));
                    else r.Add(new CheckItem("vault-family", "Backups", 0, "Hallow family backup: last one " + Ago(last.Value, now), null));
                }
            }
            if (Has(inst, "Keys"))
            {
                var st = env.Status("Keys"); object so = null;
                var sf = st != null && st.TryGetValue("safety", out so) ? so as Dictionary<string, object> : null;
                if (sf != null)
                {
                    int done = (int)Family.Num(sf, "done"), of = (int)Family.Num(sf, "of");
                    if (of > 0 && done < of) r.Add(new CheckItem("keys-safety", "Backups", 1, "Hallow Keys' safety checklist: " + done + " of " + of + " done", "Open Hallow Keys > Safety to finish it (emergency kit, backups).").Fix("Open Hallow Keys", "open", "Keys", "open", null));
                    else if (of > 0) r.Add(new CheckItem("keys-safety", "Backups", 0, "Hallow Keys' safety checklist is done", null));
                }
            }
            var auth = Path.Combine(env.Roaming, "HallowAuthenticator");
            var files = new List<FileInfo>();
            foreach (var sub in new[] { "autobackups", "backups" })
                try { var d = Path.Combine(auth, sub); if (Directory.Exists(d)) files.AddRange(new DirectoryInfo(d).GetFiles()); } catch { }
            if (files.Count > 0)
            {
                var newest = files.Max(f => f.LastWriteTime);
                if ((now - newest).TotalDays > 14) r.Add(new CheckItem("authenticator", "Backups", 1, "The Authenticator's last backup on this PC was " + Ago(newest, now), "Open Hallow Authenticator on your phone (with the PC reachable) or tap Back up to my PC."));
                else r.Add(new CheckItem("authenticator", "Backups", 0, "Authenticator backup on this PC: " + Ago(newest, now), null));
            }
            else if (Directory.Exists(auth)) r.Add(new CheckItem("authenticator", "Backups", 1, "No Authenticator backup on this PC yet", "In Hallow Authenticator on your phone: Settings > Back up to my PC."));
            if (r.Count == 0) r.Add(new CheckItem("backups", "Backups", -1, "No backups to check yet", null));
            return r;
        }

        // ---- 7. Ransomware protection (Controlled folder access) blocks - from Hallow Guard 1.10's status, and Vault's blocked destinations
        public static List<CheckItem> RansomwareCheck(CheckEnv env, List<KeyValuePair<string, string>> inst)
        {
            var r = new List<CheckItem>();
            int vaultBlocked = 0;
            if (Has(inst, "Vault"))
            {
                var st = env.Status("Vault"); object fo = null; var fam = st != null && st.TryGetValue("family", out fo) ? fo as Dictionary<string, object> : null;
                vaultBlocked = Family.List(fam, "destinations").Count(d => Family.Bool(d, "blocked"));
            }
            int? blocked = null;
            if (Has(inst, "Guard"))
            {
                var g = env.Status("Guard"); object o;
                if (g != null && g.TryGetValue("cfaBlocked", out o) && o != null) { double v; if (double.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) blocked = (int)v; }
            }
            if (vaultBlocked > 0)
                r.Add(new CheckItem("cfa-vault", "Ransomware protection", 2, "Ransomware protection blocked Hallow Vault's family backup", "Windows didn't let it write to that folder. " + (Has(inst, "Guard") ? "Hallow Guard's Ransomware page can allow it (Windows asks you first)." : "Windows Security > Ransomware protection > Allow an app, or pick another folder."))
                    .Fix(Has(inst, "Guard") ? "Open Guard's Allow helper" : "Open Windows Security", Has(inst, "Guard") ? "open" : "url", Has(inst, "Guard") ? "Guard" : null, Has(inst, "Guard") ? "open ransomware" : "windowsdefender://ransomwareprotection", null));
            if (blocked != null && blocked > 0)
                r.Add(new CheckItem("cfa", "Ransomware protection", 1, "Ransomware protection blocked " + blocked + (blocked == 1 ? " app" : " apps") + " recently", "If one is a Hallow app or a backup, allow it with Hallow Guard's Allow helper.")
                    .Fix("Open Guard's Allow helper", "open", "Guard", "open ransomware", null));
            else if (blocked == 0 && vaultBlocked == 0) r.Add(new CheckItem("cfa", "Ransomware protection", 0, "Ransomware protection hasn't blocked anything", null));
            if (r.Count == 0) r.Add(new CheckItem("cfa", "Ransomware protection", -1, Has(inst, "Guard") ? "Hallow Guard can't read the block log" : "Hallow Guard isn't installed", Has(inst, "Guard") ? "Hallow Guard 1.10 or newer shows what Ransomware protection blocked." : "It shows what Ransomware protection blocked."));
            return r;
        }

        // ---- 8. Start with Windows for the apps you run every day (Hub, Gear and the usual ones)
        // v2.23: apps whose Start with Windows is only Hallow Setup's switch (no Settings switch of their own)
        static readonly string[] NoOwnStartup = { "Capture", "Display", "Find", "Net" };   // v2.24: Find; v2.26: Net (Hallow Setup writes its "--tray" start)
        public static List<CheckItem> StartupCheck(CheckEnv env, List<KeyValuePair<string, string>> inst)
        {
            var r = new List<CheckItem>();
            if (env.StartsWithWindows == null) return new List<CheckItem> { new CheckItem("startup", "Start with Windows", -1, "Couldn't read the startup list", null) };
            var daily = new List<string> { "Hub" };
            if (Has(inst, "Gear")) daily.Add("Gear");
            // v2.25: never Hallow Files - it has no Start with Windows at all (not in its settings, not in Hallow Setup), so there's nothing to nag about
            foreach (var a in env.Usual() ?? new List<string>()) if (Has(inst, a) && !FamilyRestarter.NoteOnly.Contains(a, StringComparer.OrdinalIgnoreCase) && !FamilyRestarter.NotUsual(a) && !daily.Contains(a, StringComparer.OrdinalIgnoreCase)) daily.Add(a);
            var off = new List<string>();
            foreach (var a in daily)
            {
                bool? on = null; try { on = env.StartsWithWindows(a); } catch { }
                if (on != false) continue;
                off.Add(a);
                bool setupOnly = NoOwnStartup.Contains(a, StringComparer.OrdinalIgnoreCase);   // v2.23
                var it = new CheckItem("startup-" + a.ToLowerInvariant(), "Start with Windows", 1, "Hallow " + a + " doesn't start with Windows", a == "Hub" ? "Hub's own setting - turn it on here." :
                    setupOnly ? "Hallow Setup > Hallow " + a + " > Start with Windows (Task Manager > Startup apps can switch it off too)." : "Hallow " + a + " > Settings > Start with Windows (Task Manager > Startup apps can switch it off too).");
                if (a == "Hub") it.Fix("Turn on", "hub-startup", "Hub", null, "Hallow Hub starts in the tray when you sign in");
                else if (!setupOnly) it.Fix("Open its settings", "open", a, "open settings", "Opens Hallow " + a + "'s settings - turn on Start with Windows there");
                r.Add(it);
            }
            if (off.Count == 0) r.Add(new CheckItem("startup", "Start with Windows", 0, daily.Count == 1 ? "Hallow Hub starts with Windows" : "Your " + daily.Count + " everyday Hallow apps start with Windows", null));
            return r;
        }
    }
}
