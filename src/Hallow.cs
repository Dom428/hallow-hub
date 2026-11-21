// Hallow Hub v2.5 - Hallow family v3: the shared accent colour and (v2.15) light / dark mode (%APPDATA%\Hallow\theme.json, written by Hallow Toolkit)
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
        // v2.15: light and dark mode. The family mode comes from theme.json ("mode": dark / light / system),
        // Hub's own Look setting (hallow / light / dark) wins over it.
        public static string LookOverride = "hallow";
        public static string FamilyMode = "dark";
        public static bool Light;                      // what the app shows now
        public static string AccentText = "#B85410";   // the accent for text / thin lines on light backgrounds (>= 4.5:1 on white)
        public static string OnAccent = "#1E1A26";     // text on a filled accent button in light mode
        public static Func<bool?> SystemLight = ReadSystemLight;   // tests swap this for a fake

        // test only: HALLOW_FAMILY_TEST=<folder> reads <folder>\Hallow\theme.json instead of the real one
        public static string Dir
        {
            get
            {
                var t = Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST");
                return string.IsNullOrEmpty(t) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hallow") : Path.Combine(t, "Hallow");
            }
        }
        // theme.json as a dictionary, or null (missing / broken). Shared read: Hallow Toolkit replaces the file atomically.
        static Dictionary<string, object> ReadFile()
        {
            try
            {
                var f = Path.Combine(Dir, "theme.json"); if (!File.Exists(f)) return null;
                string text;
                using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs, Encoding.UTF8)) text = r.ReadToEnd();
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text);
            }
            catch { return null; }
        }
        // the shared accent, or null (missing, broken or not a #RRGGBB colour)
        public static string ReadShared()
        {
            try
            {
                var d = ReadFile();
                object o; if (d == null || !d.TryGetValue("accent", out o) || !(o is string)) return null;
                var s = ((string)o).Trim();
                if (s.Length != 7 || s[0] != '#' || !s.Substring(1).All(Uri.IsHexDigit)) return null;
                return s.ToUpperInvariant();
            }
            catch { return null; }
        }
        // the family mode: dark, light or system (missing / unknown / broken = dark)
        public static string ReadMode()
        {
            try
            {
                var d = ReadFile();
                object o; if (d == null || !d.TryGetValue("mode", out o) || !(o is string)) return "dark";
                return NormalMode((string)o);
            }
            catch { return "dark"; }
        }
        public static string NormalMode(string s) { s = (s ?? "").Trim().ToLowerInvariant(); return s == "light" || s == "system" ? s : "dark"; }
        public static string NormalLook(string s) { s = (s ?? "").Trim().ToLowerInvariant(); return s == "light" || s == "dark" ? s : "hallow"; }
        // Hub's Look setting wins; Follow Hallow uses the family mode; "system" asks Windows (no answer = dark)
        public static string Resolve(string over, string familyMode, Func<bool?> systemLight)
        {
            var o = NormalLook(over); if (o != "hallow") return o;
            var m = NormalMode(familyMode);
            if (m == "system") { bool? l = null; try { if (systemLight != null) l = systemLight(); } catch { } return l == true ? "light" : "dark"; }
            return m;
        }
        // Windows' app mode: AppsUseLightTheme (1 = light), null when it isn't there
        public static bool? ReadSystemLight()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    if (v is int) return (int)v != 0;
                    return null;
                }
            }
            catch { return null; }
        }
        public static bool FollowsSystem { get { return NormalLook(LookOverride) == "hallow" && FamilyMode == "system"; } }
        // true when the accent or the mode changed
        public static bool Refresh()
        {
            var want = (UseShared ? ReadShared() : null) ?? Own;
            FamilyMode = ReadMode();
            var light = Resolve(LookOverride, FamilyMode, SystemLight) == "light";
            AccentText = DarkenForWhite(want); OnAccent = TextOn(want);
            if (want == Accent && light == Light) return false;
            Accent = want; AccentLight = want == Own ? "#F59A50" : Mix(want, "#FFFFFF", 0.3); Light = light;
            return true;
        }
        public static string Mix(string a, string b, double t)
        {
            var x = (Color)ColorConverter.ConvertFromString(a); var y = (Color)ColorConverter.ConvertFromString(b);
            return string.Format("#{0:X2}{1:X2}{2:X2}", (int)(x.R + (y.R - x.R) * t), (int)(x.G + (y.G - x.G) * t), (int)(x.B + (y.B - x.B) * t));
        }

        // ---- contrast (WCAG 2) and the darkened accent
        static double Lin(double c) { c /= 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        public static double Luminance(Color c) { return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B); }
        public static double Contrast(string a, string b)
        {
            double la = Luminance((Color)ColorConverter.ConvertFromString(a)), lb = Luminance((Color)ColorConverter.ConvertFromString(b));
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }
        // keep hue and saturation, lower the lightness in 0.01 steps until it reads on white (>= 4.5:1)
        public static string DarkenForWhite(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
            double h = 0, s = 0, l = (mx + mn) / 2, d = mx - mn;
            if (d > 0)
            {
                s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
                h = mx == r ? (g - b) / d + (g < b ? 6 : 0) : mx == g ? (b - r) / d + 2 : (r - g) / d + 4; h /= 6;
            }
            var cur = hex.ToUpperInvariant();
            while (Contrast(cur, "#FFFFFF") < 4.5 && l > 0) { l = Math.Max(0, Math.Round(l - 0.01, 2)); cur = FromHsl(h, s, l); }
            return cur;
        }
        static string FromHsl(double h, double s, double l)
        {
            double r, g, b;
            if (s == 0) r = g = b = l;
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
                r = Hue(p, q, h + 1.0 / 3); g = Hue(p, q, h); b = Hue(p, q, h - 1.0 / 3);
            }
            return string.Format("#{0:X2}{1:X2}{2:X2}", (int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
        }
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        // text on a filled accent: dark #1E1A26 or white, whichever reads better
        public static string TextOn(string hex) { return Contrast(hex, "#1E1A26") >= Contrast(hex, "#FFFFFF") ? "#1E1A26" : "#FFFFFF"; }

        // the light palette: each colour Hub uses in dark mode -> its light-mode value (dark mode = today's values, unchanged)
        public static readonly Dictionary<string, string> LightMap = new Dictionary<string, string> {
            { "#16121C", "#F6F4FA" }, { "#0E0B12", "#F6F4FA" },                                     // background
            { "#211B2A", "#FFFFFF" }, { "#241E2E", "#FFFFFF" }, { "#1C1724", "#FFFFFF" }, { "#1A1522", "#FFFFFF" },   // panels, pop-ups, fields
            { "#2E2639", "#EEEAF5" }, { "#2A2333", "#EEEAF5" },                                     // panel 2 / buttons / hover
            { "#3A3248", "#DAD3E6" }, { "#55496A", "#B9AFCB" },                                     // lines, hover line
            { "#ECE8F4", "#1E1A26" }, { "#D6D0E2", "#3A3446" }, { "#CFC8DC", "#423B50" },           // text
            { "#8F88A0", "#6B6478" }, { "#6F6880", "#6B6478" },                                     // muted
            { "#5A5268", "#A79FB8" }, { "#5A5366", "#A79FB8" }, { "#58566A", "#ABA3BC" }, { "#CDCAD6", "#FFFFFF" },   // empty stars, offline dot, switch off
            { "#A98BE0", "#7A5CC0" }, { "#C9B2F5", "#6A4CB0" }, { "#B48BF0", "#7A4FD0" },           // lilac
            { "#6ED28C", "#1E8E4F" }, { "#8EE0A6", "#1E8E4F" }, { "#0E2416", "#FFFFFF" },           // green, text on green
            { "#F0BE5A", "#A66A00" }, { "#E0A050", "#A66A00" }, { "#241A04", "#FFFFFF" },           // amber, text on amber
            { "#F05A5A", "#C2333F" }, { "#F0505A", "#C2333F" }, { "#C8414B", "#C2333F" }, { "#E05A64", "#A82A35" },   // red
            { "#5AB4F0", "#1C68A5" }, { "#8FCBF5", "#1C68A5" }, { "#4AA8FF", "#1A66B8" }, { "#5A8CF0", "#3158C0" },   // blues (store colours)
            { "#F05A8C", "#C0306A" },
        };
        // every place that asks for Hub's orange gets the accent; per-game colours (cover glows) never come through here
        public static string Hex(string hex)
        {
            if (hex == null) return hex;
            if (!Light) return DarkHex(hex);
            var h = hex.ToUpperInvariant();
            if (h == "#F07820") return AccentText;
            if (h == "#F59A50" || h == "#FF9A45") return Accent;
            if (h == "#1A0E04") return OnAccent;
            string l; return LightMap.TryGetValue(h, out l) ? l : hex;
        }
        // the dark-mode colour (things drawn on top of game art stay as they are in both modes)
        public static string DarkHex(string hex)
        {
            if (Accent == Own || hex == null) return hex;
            var h = hex.ToUpperInvariant();
            if (h == "#F07820") return Accent;
            if (h == "#F59A50" || h == "#FF9A45") return AccentLight;
            return hex;
        }
        // a filled accent area (Play, checked chips): the real accent in both modes
        public static string Fill(string hex) { return hex != null && hex.ToUpperInvariant() == "#F07820" ? Accent : Hex(hex); }
        // the theme file with the accent in place of the orange (and the light palette in light mode)
        public static string Theme(string xaml)
        {
            if (!Light)
            {
                if (Accent == Own) return xaml;
                return xaml.Replace("#F07820", Accent).Replace("#F59A50", AccentLight).Replace("#FF9A45", AccentLight);
            }
            // filled accent buttons keep the real accent; the hover veil darkens instead of lightening
            var x = xaml.Replace("<Setter Property=\"Background\" Value=\"#F07820\"/>", "<Setter Property=\"Background\" Value=\"@FILL\"/>")
                .Replace("<Setter Property=\"BorderBrush\" Value=\"#F59A50\"/>", "<Setter Property=\"BorderBrush\" Value=\"@FILL\"/>")
                .Replace("<SolidColorBrush x:Key=\"CheckedBg\" Color=\"#F07820\"/>", "<SolidColorBrush x:Key=\"CheckedBg\" Color=\"@FILL\"/>")
                .Replace("<SolidColorBrush x:Key=\"CheckedLine\" Color=\"#F59A50\"/>", "<SolidColorBrush x:Key=\"CheckedLine\" Color=\"@FILL\"/>")
                .Replace("Background=\"#FFFFFF\" Opacity=\"0\"", "Background=\"@INK\" Opacity=\"0\"");
            x = System.Text.RegularExpressions.Regex.Replace(x, "#[0-9A-Fa-f]{6}\\b", m => Hex(m.Value));
            return x.Replace("@FILL", Accent).Replace("@INK", "#1E1A26");
        }

        // ---- the window side: scrims, shadows, glows and the Windows title bar
        public static Color Scrim(byte alpha) { return Light ? Color.FromArgb(alpha, 0xF6, 0xF4, 0xFA) : Color.FromArgb(alpha, 0x12, 0x0F, 0x18); }
        // a soft shadow under a floating panel (softer in light mode)
        public static System.Windows.Media.Effects.Effect Shadow(double blur, double opacity, double depth)
        {
            if (Light) return new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = blur * 0.8, Opacity = opacity * 0.25, ShadowDepth = Math.Max(1, depth / 2), Color = Color.FromRgb(0x3A, 0x2F, 0x50) };
            return new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = blur, Opacity = opacity, ShadowDepth = depth };
        }
        // a coloured glow - none in light mode (the coloured border stays)
        public static System.Windows.Media.Effects.Effect Glow(Color c, double blur, double opacity)
        {
            if (Light) return null;
            return new System.Windows.Media.Effects.DropShadowEffect { Color = c, BlurRadius = blur, ShadowDepth = 0, Opacity = opacity };
        }
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        // the title bar matches the mode (DWMWA_USE_IMMERSIVE_DARK_MODE = 20); also when it switches live
        public static void TitleBar(System.Windows.Window w)
        {
            try
            {
                var h = new System.Windows.Interop.WindowInteropHelper(w).Handle; if (h == IntPtr.Zero) return;
                int dark = Light ? 0 : 1; DwmSetWindowAttribute(h, 20, ref dark, 4);
            }
            catch { }
        }
        public static void Dress(System.Windows.Window w) { w.SourceInitialized += (s, e) => TitleBar(w); }
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
