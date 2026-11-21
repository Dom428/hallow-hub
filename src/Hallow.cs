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
        public static string AccentFill = Own;         // v2.16: a filled accent area in light mode (buttons, pills, picked chips) - Hallow Toolkit 5.16's rule
        public static string AccentFillHover = "#D86C1C";   // the fill under the mouse (fill x 0.9)
        // v2.16: light-mode status colours as small text (the icon / dot values are just under 4.5:1 on white) and the lilac
        // for text, dark enough for Panel 2 (#EEEAF5) too
        public const string GreenText = "#1B7A44", AmberText = "#8F5B00", GreenIcon = "#1E8E4F", AmberIcon = "#A66A00";
        public static readonly string LilacText = Darken("#7A5CC0", 4.5, "#EEEAF5");
        // the light accents from the user's accent: text / thin lines darkened against Panel 2 (#EEEAF5, the darkest surface they
        // sit on), the fill the real accent unless neither text colour reaches 4.5:1 on it (then darkened against white, white text)
        public static void SetAccents(string want)
        {
            AccentText = Darken(want, 4.5, "#EEEAF5");
            AccentFill = Contrast(want, TextOn(want)) >= 4.5 ? want.ToUpperInvariant() : Darken(want, 4.5, "#FFFFFF");
            OnAccent = TextOn(AccentFill);
            var f = (Color)ColorConverter.ConvertFromString(AccentFill);
            AccentFillHover = string.Format("#{0:X2}{1:X2}{2:X2}", (int)(f.R * 0.9), (int)(f.G * 0.9), (int)(f.B * 0.9));
        }
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
            SetAccents(want);
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
        public static string DarkenForWhite(string hex) { return Darken(hex, 4.5, "#FFFFFF"); }
        // v2.16: exactly Hallow Toolkit 5.16's Get-LightTextAccent: the colour as it is if it reaches min on the surface, else its HSL
        // (System.Drawing GetHue / GetSaturation / GetBrightness) with the lightness lowered 0.01 at a time to the first colour that does
        public static string Darken(string hex, double min, string on)
        {
            var cur = hex.ToUpperInvariant();
            if (Contrast(cur, on) >= min) return cur;
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var dc = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
            double h = dc.GetHue() / 360.0, s = dc.GetSaturation(), l = dc.GetBrightness();
            while (l > 0)
            {
                l = Math.Max(0.0, l - 0.01); cur = FromHsl(h, s, l);
                if (Contrast(cur, on) >= min) break;
            }
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
            { "#A98BE0", LilacText }, { "#C9B2F5", "#6A4CB0" }, { "#B48BF0", "#7A4FD0" },           // lilac (v2.16: darker, reads on Panel 2 too)
            { "#6ED28C", GreenText }, { "#8EE0A6", GreenText }, { "#0E2416", "#FFFFFF" },           // green, text on green (v2.16: the text value; icons / dots: Icon())
            { "#F0BE5A", AmberText }, { "#E0A050", AmberText }, { "#241A04", "#FFFFFF" },           // amber, text on amber (v2.16: the text value; icons / dots: Icon())
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
            if (h == "#F59A50" || h == "#FF9A45") return AccentFill;
            if (h == "#1A0E04") return OnAccent;
            string l; return LightMap.TryGetValue(h, out l) ? l : hex;
        }
        // v2.16: an icon, a dot or a fill with no text on it - green / amber keep their icon values there (3:1 is enough)
        public static string Icon(string hex)
        {
            if (hex == null || !Light) return Hex(hex);
            var h = hex.ToUpperInvariant();
            if (h == "#6ED28C" || h == "#8EE0A6") return GreenIcon;
            if (h == "#F0BE5A" || h == "#E0A050") return AmberIcon;
            return Hex(hex);
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
        // a filled accent area (Play, checked chips): the real accent in dark mode; in light mode the light fill (v2.16: the real accent
        // unless its text would read under 4.5:1 - violet #8B5CF6 with white text is 4.23:1 - then a darker step with white text)
        public static string Fill(string hex) { return hex != null && hex.ToUpperInvariant() == "#F07820" ? (Light ? AccentFill : Accent) : Hex(hex); }
        // the theme file with the accent in place of the orange (and the light palette in light mode)
        public static string Theme(string xaml)
        {
            if (!Light)
            {
                if (Accent == Own) return xaml;
                return xaml.Replace("#F07820", Accent).Replace("#F59A50", AccentLight).Replace("#FF9A45", AccentLight);
            }
            // filled accent buttons: the light fill (v2.16), x 0.9 under the mouse; elsewhere the hover veil darkens instead of lightening
            xaml = System.Text.RegularExpressions.Regex.Replace(xaml, "(<Setter Property=\"Foreground\" Value=\"#1A0E04\"/>\\s*<Setter Property=\"FontWeight\" Value=\"SemiBold\"/>)(\\s*</Style>)",
                "$1<Style.Resources><SolidColorBrush x:Key=\"HoverInk\" Color=\"Transparent\"/></Style.Resources>" +
                "<Style.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter Property=\"Background\" Value=\"@FILLHOVER\"/></Trigger></Style.Triggers>$2");
            xaml = xaml.Replace("<SolidColorBrush x:Key=\"Bg\"", "<SolidColorBrush x:Key=\"HoverInk\" Color=\"@INK\"/>\n    <SolidColorBrush x:Key=\"Bg\"");
            var x = xaml.Replace("<Setter Property=\"Background\" Value=\"#F07820\"/>", "<Setter Property=\"Background\" Value=\"@FILL\"/>")
                .Replace("<Setter Property=\"BorderBrush\" Value=\"#F59A50\"/>", "<Setter Property=\"BorderBrush\" Value=\"@FILL\"/>")
                .Replace("<SolidColorBrush x:Key=\"CheckedBg\" Color=\"#F07820\"/>", "<SolidColorBrush x:Key=\"CheckedBg\" Color=\"@FILL\"/>")
                .Replace("<SolidColorBrush x:Key=\"CheckedLine\" Color=\"#F59A50\"/>", "<SolidColorBrush x:Key=\"CheckedLine\" Color=\"@FILL\"/>")
                .Replace("Background=\"#FFFFFF\" Opacity=\"0\"", "Background=\"{DynamicResource HoverInk}\" Opacity=\"0\"")
                .Replace("<Setter TargetName=\"track\" Property=\"Background\" Value=\"#6ED28C\"/>", "<Setter TargetName=\"track\" Property=\"Background\" Value=\"" + GreenIcon + "\"/>");   // v2.16: a fill - icon green
            x = System.Text.RegularExpressions.Regex.Replace(x, "#[0-9A-Fa-f]{6}\\b", m => Hex(m.Value));
            return x.Replace("@FILLHOVER", AccentFillHover).Replace("@FILL", AccentFill).Replace("@INK", "#1E1A26");
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

        // ---- v2.16: windows already open restyle live when the mode or the accent changes (not only when reopened)
        // Every brush made from a palette colour remembers where it came from ('h' Hex, 'f' Fill, 'd' DarkHex, 'i' Icon + the colour).
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SolidColorBrush, string> made = new System.Runtime.CompilerServices.ConditionalWeakTable<SolidColorBrush, string>();
        public static SolidColorBrush Brush(string hex, char kind)
        {
            var v = kind == 'f' ? Fill(hex) : kind == 'd' ? DarkHex(hex) : kind == 'i' ? Icon(hex) : Hex(hex);
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(v));
            made.Add(b, kind + hex); return b;
        }
        public static SolidColorBrush Brush(string hex) { return Brush(hex, 'h'); }
        sealed class Same : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); }
        }
        // after the theme was reloaded: walk a window (and its menus / tool tips) and swap what came from the old theme (styles,
        // brushes looked up by key) for the new theme's, and every palette brush for its colour now. Returns how many values changed.
        public static int Restyle(System.Windows.DependencyObject root, System.Windows.ResourceDictionary oldRd, System.Windows.ResourceDictionary newRd)
        {
            var keyOf = new Dictionary<object, object>(new Same());
            if (oldRd != null) foreach (var k in oldRd.Keys) { try { var v = oldRd[k]; if (v != null && !keyOf.ContainsKey(v)) keyOf[v] = k; } catch { } }
            var seen = new HashSet<object>(new Same()); int n = 0;
            var todo = new Stack<System.Windows.DependencyObject>(); todo.Push(root);
            while (todo.Count > 0)
            {
                var d = todo.Pop(); if (d == null || !seen.Add(d)) continue;
                var sets = new List<KeyValuePair<System.Windows.DependencyProperty, object>>();
                var e = d.GetLocalValueEnumerator();
                while (e.MoveNext())
                {
                    var p = e.Current.Property; var v = e.Current.Value; if (p.ReadOnly || v == null) continue;
                    object key; string tag; var sb = v as SolidColorBrush;
                    if (keyOf.TryGetValue(v, out key)) { if (newRd != null && newRd.Contains(key)) sets.Add(new KeyValuePair<System.Windows.DependencyProperty, object>(p, newRd[key])); }
                    else if (sb != null && made.TryGetValue(sb, out tag)) sets.Add(new KeyValuePair<System.Windows.DependencyProperty, object>(p, Brush(tag.Substring(1), tag[0])));
                    else if (v is System.Windows.FrameworkElement && !(v is System.Windows.Window)) todo.Push((System.Windows.DependencyObject)v);   // a ContextMenu / ToolTip held in a property
                }
                foreach (var s in sets) { try { d.SetValue(s.Key, s.Value); n++; } catch { } }
                foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(d)) { var cd = c as System.Windows.DependencyObject; if (cd != null) todo.Push(cd); }
                if (d is Visual || d is System.Windows.Media.Media3D.Visual3D)
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) todo.Push(VisualTreeHelper.GetChild(d, i));
            }
            return n;
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
