// Hallow Hub v2.5 - Hallow family v3: the shared accent colour (%APPDATA%\Hallow\theme.json, written by Hallow Toolkit)
// and crash reports kept on this PC (%APPDATA%\HallowHub\crashes). Nothing here is ever sent anywhere.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Media;

namespace HallowHub
{
    public static class HallowLook
    {
        public const string Own = "#F07820";          // Hub's own orange
        public static string Accent = Own;             // what the app uses now (#RRGGBB)
        public static string AccentLight = "#F59A50";  // a lighter step of it (button edges, gradients)
        public static bool UseShared = true;           // the "Use the Hallow accent" setting

        // test only: HALLOW_FAMILY_TEST=<folder> reads <folder>\Hallow\theme.json instead of the real one
        public static string Dir
        {
            get
            {
                var t = Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST");
                return string.IsNullOrEmpty(t) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hallow") : Path.Combine(t, "Hallow");
            }
        }
        // the shared accent, or null (missing, broken or not a #RRGGBB colour)
        public static string ReadShared()
        {
            try
            {
                var f = Path.Combine(Dir, "theme.json"); if (!File.Exists(f)) return null;
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(f, Encoding.UTF8));
                object o; if (d == null || !d.TryGetValue("accent", out o) || !(o is string)) return null;
                var s = ((string)o).Trim();
                if (s.Length != 7 || s[0] != '#' || !s.Substring(1).All(Uri.IsHexDigit)) return null;
                return s.ToUpperInvariant();
            }
            catch { return null; }
        }
        // true when the colour changed
        public static bool Refresh()
        {
            var want = (UseShared ? ReadShared() : null) ?? Own;
            if (want == Accent) return false;
            Accent = want; AccentLight = want == Own ? "#F59A50" : Mix(want, "#FFFFFF", 0.3);
            return true;
        }
        public static string Mix(string a, string b, double t)
        {
            var x = (Color)ColorConverter.ConvertFromString(a); var y = (Color)ColorConverter.ConvertFromString(b);
            return string.Format("#{0:X2}{1:X2}{2:X2}", (int)(x.R + (y.R - x.R) * t), (int)(x.G + (y.G - x.G) * t), (int)(x.B + (y.B - x.B) * t));
        }
        // every place that asks for Hub's orange gets the accent; per-game colours (cover glows) never come through here
        public static string Hex(string hex)
        {
            if (Accent == Own || hex == null) return hex;
            var h = hex.ToUpperInvariant();
            if (h == "#F07820") return Accent;
            if (h == "#F59A50" || h == "#FF9A45") return AccentLight;
            return hex;
        }
        // the theme file with the accent in place of the orange
        public static string Theme(string xaml)
        {
            if (Accent == Own) return xaml;
            return xaml.Replace("#F07820", Accent).Replace("#F59A50", AccentLight).Replace("#FF9A45", AccentLight);
        }
    }

    public static class Crash
    {
        public static string Dir
        {
            get
            {
                var t = Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST");
                return Path.Combine(string.IsNullOrEmpty(t) ? HubSettings.Dir : Path.Combine(t, "HallowHub"), "crashes");
            }
        }
        // catch what nobody else did: write a report (the app still closes as before)
        public static void Hook(System.Windows.Application app)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Write(e.ExceptionObject as Exception, "app");
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => { Write(e.Exception, "background task"); e.SetObserved(); };
            if (app != null) app.DispatcherUnhandledException += (s, e) => Write(e.Exception, "window");
        }
        static bool writing;
        // app, version, Windows, time and the exception with its inner ones - never settings, keys or file contents
        public static string Write(Exception ex, string where)
        {
            if (writing || ex == null) return null;
            writing = true;
            try
            {
                Directory.CreateDirectory(Dir);
                var now = DateTime.Now; var f = Path.Combine(Dir, now.ToString("yyyy-MM-dd HH-mm-ss") + ".txt");
                var sb = new StringBuilder();
                sb.AppendLine("Hallow Hub " + Program.Version + " closed unexpectedly");
                sb.AppendLine("Time: " + now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Windows: " + WindowsName() + (Environment.Is64BitOperatingSystem ? " (64-bit)" : ""));
                sb.AppendLine(".NET: " + Environment.Version);
                sb.AppendLine("Where: " + where);
                sb.AppendLine();
                var e = ex; int depth = 0;
                while (e != null && depth < 8)
                {
                    if (depth > 0) sb.AppendLine().AppendLine("--- inner exception ---");
                    sb.AppendLine(e.GetType().FullName + ": " + e.Message);
                    sb.AppendLine(e.StackTrace ?? "(no stack)");
                    e = e.InnerException; depth++;
                }
                File.WriteAllText(f, sb.ToString(), new UTF8Encoding(false));
                foreach (var old in Directory.GetFiles(Dir, "*.txt").OrderByDescending(x => x, StringComparer.Ordinal).Skip(20)) { try { File.Delete(old); } catch { } }
                return f;
            }
            catch { return null; }
            finally { writing = false; }
        }
        static string WindowsName()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    if (k != null) return (k.GetValue("ProductName") + " " + k.GetValue("DisplayVersion") + " (build " + k.GetValue("CurrentBuildNumber") + ")").Trim();
            }
            catch { }
            return Environment.OSVersion.VersionString;
        }
        public static string Newest()
        {
            try { return Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.txt").OrderByDescending(x => x, StringComparer.Ordinal).FirstOrDefault() : null; } catch { return null; }
        }
        public static DateTime? When(string file)
        {
            DateTime t;
            return file != null && DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out t) ? t : (DateTime?)null;
        }
    }
}
