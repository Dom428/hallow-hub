// Hallow Hub v2.2 - the Hallow family link (v1): reads Vault, Clips and Overlay's status.json, sends them one-line commands
// over their named pipes, and answers ping / status on Hub's own pipe. Everything here fails quietly: a missing, old or
// broken app just hides its card.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowHub
{
    public static class Family
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool WaitNamedPipe(string name, uint timeout);

        // Test only: HALLOW_FAMILY_TEST=<folder> reads <folder>\Hallow<App>\status.json, counts <folder>\Programs\Hallow <App>\Hallow<App>.exe
        // as installed, and adds ".test" to every pipe name (Hub's own too), so tests never touch the real apps.
        static string TestDir { get { var t = Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST"); return string.IsNullOrEmpty(t) ? null : t; } }
        static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        static string Roaming { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }

        public static string Exe(string app)
        {
            var root = TestDir != null ? Path.Combine(TestDir, "Programs") : Path.Combine(Local, "Programs");
            return Path.Combine(root, "Hallow " + app, "Hallow" + app + ".exe");
        }
        public static bool Installed(string app) { try { return File.Exists(Exe(app)); } catch { return false; } }
        public static string DataDir(string app)
        {
            if (TestDir != null) return Path.Combine(TestDir, "Hallow" + app);
            return Path.Combine(app == "Clips" ? Local : Roaming, "Hallow" + app);
        }
        public static string PipeName(string app) { return "HallowFamily." + app + "." + Environment.UserName + (TestDir != null ? ".test" : ""); }

        // the app's status.json, or null (missing, half-written or not JSON)
        public static Dictionary<string, object> Status(string app)
        {
            try
            {
                var f = Path.Combine(DataDir(app), "status.json"); if (!File.Exists(f)) return null;
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(f, Encoding.UTF8));
            }
            catch { return null; }
        }
        public static DateTime StatusTime(string app) { try { return File.GetLastWriteTimeUtc(Path.Combine(DataDir(app), "status.json")); } catch { return DateTime.MinValue; } }

        // is its pipe there? (doesn't connect, returns at once)
        public static bool Running(string app)
        {
            try
            {
                if (WaitNamedPipe(@"\\.\pipe\" + PipeName(app), 1)) return true;
                var err = Marshal.GetLastWin32Error();
                return err == 121 || err == 231;   // timeout / busy: it's there, just serving someone else
            }
            catch { return false; }
        }

        // v2.13: the Hallow apps whose pipe is open right now ("HallowFamily.<App>.<user>", ".test" added in tests) - names only,
        // nothing is connected to. Lets Hub's menu host a Hallow app it doesn't know by name yet.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct FindData
        {
            public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
            public uint SizeHigh, SizeLow, Reserved0, Reserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string ShortName;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr FindFirstFile(string name, out FindData d);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool FindNextFile(IntPtr h, out FindData d);
        [DllImport("kernel32.dll")] static extern bool FindClose(IntPtr h);
        public static List<string> PipeApps()
        {
            var r = new List<string>();
            const string pre = "HallowFamily.";
            var post = "." + Environment.UserName + (TestDir != null ? ".test" : "");
            FindData d; var h = FindFirstFile(@"\\.\pipe\*", out d);
            if (h == IntPtr.Zero || h == new IntPtr(-1)) return r;
            try
            {
                int n = 0;
                do
                {
                    var p = d.Name ?? "";
                    if (p.Length > pre.Length + post.Length && p.StartsWith(pre, StringComparison.OrdinalIgnoreCase) && p.EndsWith(post, StringComparison.OrdinalIgnoreCase))
                    {
                        var app = p.Substring(pre.Length, p.Length - pre.Length - post.Length);
                        if (app.Length <= 24 && app.All(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) && !r.Contains(app, StringComparer.OrdinalIgnoreCase)) r.Add(app);
                    }
                } while (++n < 5000 && FindNextFile(h, out d));
            }
            finally { FindClose(h); }
            return r;
        }

        // One command, one reply line ("ok", "ok {...}", "error ..."), or null when the app isn't there or doesn't answer.
        // Blocks up to ~6 s: call it off the window's thread.
        public static string Send(string app, string line) { return Send(app, line, 1500, 6000); }
        // v2.4: with your own limits (the tray menu asks each app for its items with 300 ms)
        public static string Send(string app, string line, int connectMs, int replyMs)
        {
            if (!Running(app)) return null;
            string reply = null;
            var c = new NamedPipeClientStream(".", PipeName(app), PipeDirection.InOut);
            var until = DateTime.UtcNow.AddMilliseconds(connectMs + replyMs);
            try
            {
                c.Connect(connectMs);
                var t = new Thread(() =>
                {
                    try
                    {
                        var b = new UTF8Encoding(false).GetBytes(line.Replace("\r", " ").Replace("\n", " ") + "\n");
                        c.Write(b, 0, b.Length); c.Flush();
                        reply = new StreamReader(c, new UTF8Encoding(false)).ReadLine();
                    }
                    catch { }
                }) { IsBackground = true };
                t.Start();
                if (!t.Join(Math.Max(1, (int)(until - DateTime.UtcNow).TotalMilliseconds))) reply = null;
            }
            catch { reply = null; }
            finally { try { c.Dispose(); } catch { } }
            return reply == null ? null : reply.Trim();
        }
        // v2.19: percentages are written without a space ("45%", not "45 %") in everything Hub shows - also text other apps hand it
        public static string Pct(string s) { return s == null ? null : System.Text.RegularExpressions.Regex.Replace(s, "(\\d)[ \u00A0\u202F]+%", "$1%"); }
        public static bool Ok(string reply) { return reply != null && (reply == "ok" || reply.StartsWith("ok ")); }

        // small JSON helpers (readers tolerate missing fields)
        public static string Str(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? o.ToString() : null; }
        public static bool Bool(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is bool && (bool)o; }
        public static double Num(Dictionary<string, object> d, string k)
        {
            object o; double v;
            return d != null && d.TryGetValue(k, out o) && o != null && double.TryParse(Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0;
        }
        public static List<Dictionary<string, object>> List(Dictionary<string, object> d, string k)
        {
            var r = new List<Dictionary<string, object>>(); object o;
            if (d != null && d.TryGetValue(k, out o) && o is ArrayList)
                foreach (var x in (ArrayList)o) { var e = x as Dictionary<string, object>; if (e != null) r.Add(e); }
            return r;
        }
        public static DateTime? Time(Dictionary<string, object> d, string k)
        {
            var s = Str(d, k); DateTime t;
            return s != null && DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out t) ? t : (DateTime?)null;
        }
    }

    // Hub's own pipe: answers ping and status (the same JSON as %APPDATA%\HallowHub\status.json). Current user only, never the network.
    public class FamilyServer
    {
        public volatile string StatusJson = "{}";
        public Func<string, string, string> Extra;   // v2.4: (command, argument) -> reply, or null for "unknown command"
        public void Start()
        {
            new Thread(Loop) { IsBackground = true, Name = "Hallow family pipe" }.Start();
        }
        void Loop()
        {
            int fails = 0;
            while (fails < 20)
            {
                NamedPipeServerStream s = null;
                try
                {
                    var ps = new PipeSecurity();
                    ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
                    ps.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    s = new NamedPipeServerStream(Family.PipeName("Hub"), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, ps);
                    s.WaitForConnection();
                    var line = ReadLine(s, 3000);
                    if (line != null)
                    {
                        var b = new UTF8Encoding(false).GetBytes(Answer(line) + "\n");
                        s.Write(b, 0, b.Length); s.Flush();
                        try { s.WaitForPipeDrain(); } catch { }
                    }
                    fails = 0;
                }
                catch { fails++; Thread.Sleep(500); }
                finally { if (s != null) { try { if (s.IsConnected) s.Disconnect(); } catch { } s.Dispose(); } }
            }
        }
        // one line, at most 4 KB, within the time limit (a client that connects and says nothing can't hold the pipe)
        static string ReadLine(Stream s, int ms)
        {
            var buf = new List<byte>(); var one = new byte[256]; var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (buf.Count < 4096)
            {
                var left = (int)(until - DateTime.UtcNow).TotalMilliseconds; if (left <= 0) return null;
                var ar = s.BeginRead(one, 0, one.Length, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(left)) return null;
                int n = s.EndRead(ar); if (n <= 0) break;
                for (int i = 0; i < n; i++) { if (one[i] == (byte)'\n') return Encoding.UTF8.GetString(buf.ToArray()).Trim(); buf.Add(one[i]); }
            }
            return buf.Count > 0 ? Encoding.UTF8.GetString(buf.ToArray()).Trim() : null;
        }
        string Answer(string line)
        {
            var cmd = line.Split(new[] { ' ' }, 2)[0].ToLowerInvariant();
            if (cmd == "ping") return "ok {\"app\":\"Hub\",\"version\":\"" + Program.Version + "\"}";
            if (cmd == "status") return "ok " + StatusJson;
            var ex = Extra;
            if (ex != null)
            {
                var parts = line.Split(new[] { ' ' }, 2);
                try { var r = ex(cmd, parts.Length > 1 ? parts[1] : ""); if (r != null) return r; } catch { return "error " + cmd + " failed"; }
            }
            return "error unknown command";
        }
    }
    // v2.4: One Hallow icon - what Hub's tray menu shows for the other Hallow apps
    public class FamilyApp { public string App; public bool Running; public List<Dictionary<string, object>> Items = new List<Dictionary<string, object>>(); public bool Answered; public bool Known = true; }
    public static class FamilyTray
    {
        // v2.11: Hallow Keys; v2.13: Hallow Guard and Hallow Assist. The known apps keep this order (and get "Start" while they're closed).
        public static readonly string[] Apps = { "Mixer", "Vault", "Overlay", "Clips", "Remote", "Keys", "Guard", "Assist" };
        // v2.17: the newer apps - in this order after the known ones while they run (so they don't jump around A-Z); the installed ones
        // that aren't running go under one "Start a Hallow app" submenu instead of a Start line each
        public static readonly string[] Newer = { "VPN", "Torrents", "Browser", "Tasks", "Email", "Financials", "Home", "Capture", "Display" };   // v2.23: Capture, Display
        public static bool IsNewer(string app) { return Newer.Contains(app, StringComparer.OrdinalIgnoreCase); }
        public static List<string> Startable() { return Newer.Where(a => Family.Installed(a) && !Family.Running(a)).ToList(); }
        // Browser has no tray icon - it starts with its window; the others start in the tray
        public static string StartArgs(string app) { return string.Equals(app, "Browser", StringComparison.OrdinalIgnoreCase) ? "" : "--tray"; }
        // v2.13: never a submenu of their own - Hub itself, Setup (an installer) and Toolkit (it has "Open Hallow Toolkit")
        static readonly string[] NotHosted = { "Hub", "Setup", "Toolkit" };
        public static bool Hostable(string app) { return !NotHosted.Contains(app, StringComparer.OrdinalIgnoreCase); }

        // v2.13: Hub's "tray-host" answer. "apps" = the apps Hub's menu always knows; "generic" = any other Hallow app whose pipe
        // answers "menu" gets a submenu too (found by its pipe name), so a new Hallow app can hide its own icon without a Hub update.
        public static string HostJson(bool on)
        {
            return "{\"on\":" + (on ? "true" : "false") + ",\"apps\":[" + string.Join(",", Apps.Concat(Newer).Select(a => "\"" + a + "\"")) + "],\"generic\":true}";
        }

        // installed known apps (in their order), then other running Hallow apps that answer "menu" (A-Z); for the running ones,
        // their menu items (asked in parallel, 300 ms each; a known app that doesn't answer just gets Open / Exit, an unknown one is left out)
        public static List<FamilyApp> Fetch()
        {
            var list = new List<FamilyApp>();
            foreach (var a in Apps) if (Family.Installed(a)) list.Add(new FamilyApp { App = a, Running = Family.Running(a) });
            foreach (var a in Newer) if (Family.Running(a)) list.Add(new FamilyApp { App = a, Running = true });   // v2.17: running newer apps, in their order
            try
            {
                foreach (var a in Family.PipeApps().OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    if (Hostable(a) && !Apps.Contains(a, StringComparer.OrdinalIgnoreCase) && !IsNewer(a) && !list.Any(x => string.Equals(x.App, a, StringComparison.OrdinalIgnoreCase)))
                        list.Add(new FamilyApp { App = a, Running = true, Known = false });
            }
            catch { }
            var threads = new List<Thread>();
            foreach (var fa in list)
            {
                if (!fa.Running) continue;
                var f = fa;
                var th = new Thread(() =>
                {
                    try
                    {
                        var r = Family.Send(f.App, "menu", 300, 300);
                        if (r == null || !r.StartsWith("ok {")) return;
                        var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(r.Substring(3));
                        f.Items = Family.List(d, "items").Take(12).ToList(); f.Answered = true;
                    }
                    catch { }
                }) { IsBackground = true };
                th.Start(); threads.Add(th);
            }
            var until = DateTime.UtcNow.AddMilliseconds(700);
            foreach (var th in threads) th.Join(Math.Max(1, (int)(until - DateTime.UtcNow).TotalMilliseconds));
            return list.Where(x => x.Known || x.Answered).ToList();
        }
    }
}