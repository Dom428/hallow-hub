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
    public class FamilyApp { public string App; public bool Running; public List<Dictionary<string, object>> Items = new List<Dictionary<string, object>>(); public bool Answered; }
    public static class FamilyTray
    {
        public static readonly string[] Apps = { "Mixer", "Vault", "Overlay", "Clips", "Remote", "Keys" };   // v2.11: Hallow Keys
        // installed apps only; for the running ones, their menu items (asked in parallel, 300 ms each; an app that doesn't answer just gets Open / Exit)
        public static List<FamilyApp> Fetch()
        {
            var list = new List<FamilyApp>();
            foreach (var a in Apps) if (Family.Installed(a)) list.Add(new FamilyApp { App = a, Running = Family.Running(a) });
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
            return list;
        }
    }
}