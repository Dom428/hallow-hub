// Hallow Hub - talks to Hallow Mixer (its local API) and Hallow Toolkit (game session on/off).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Web.Script.Serialization;

namespace HallowHub
{
    public static class Mixer
    {
        static string ApiFile { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"HallowMixer\api.json"); } }

        static Dictionary<string, object> Call(string path)
        {
            try
            {
                if (!File.Exists(ApiFile)) return null;
                var js = new JavaScriptSerializer();
                var a = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ApiFile));
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + a["port"] + path);
                req.Headers["X-Hallow-Key"] = a["key"] as string; req.Timeout = 1500; req.ReadWriteTimeout = 1500; req.Proxy = null;
                using (var r = req.GetResponse()) using (var s = new StreamReader(r.GetResponseStream()))
                    return js.Deserialize<Dictionary<string, object>>(s.ReadToEnd());
            }
            catch { return null; }
        }
        // The mixer's presets, or null when Hallow Mixer isn't running (or its API is off).
        public static List<string> Presets()
        {
            var st = Call("/state"); object p;
            if (st == null || !st.TryGetValue("presets", out p) || !(p is System.Collections.ArrayList)) return null;
            return ((System.Collections.ArrayList)p).Cast<object>().Select(x => x as string).Where(x => x != null).ToList();
        }
        public static bool UsePreset(string name) { return Call("/preset?name=" + Uri.EscapeDataString(name)) != null; }
    }

    public static class Toolkit
    {
        public static string Script
        {
            get
            {
                var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"HallowToolkit\HallowToolkit.ps1");
                return File.Exists(p) ? p : null;
            }
        }
        // Hallow Toolkit 5.2+ understands -Mode SessionStart / SessionEnd (closes Boost apps, High performance power; puts it back after).
        public static bool CanSession
        {
            get { var s = Script; try { return s != null && File.ReadAllText(s).Contains("'SessionStart'"); } catch { return false; } }
        }
        public static void Session(bool on)
        {
            var s = Script; if (s == null) return;
            try
            {
                Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + s + "\" -Mode " + (on ? "SessionStart" : "SessionEnd"))
                { UseShellExecute = false, CreateNoWindow = true });
            }
            catch { }
        }
    }
}
