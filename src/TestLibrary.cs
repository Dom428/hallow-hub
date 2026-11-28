// Hallow Hub - v2.28: a made-up game library for tests and --snap. With HALLOW_FAMILY_TEST=<folder> (which --snap now always sets,
// to a temp folder if you didn't) Hub never looks at your real launchers, Steam accounts, Hallow Mixer or Hub settings: the games
// below are invented, their art is drawn here into <folder>\HallowHub\testart, and their playtime is made up too.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HallowHub
{
    public static class TestLibrary
    {
        public static string Root { get { var t = Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST"); return string.IsNullOrEmpty(t) ? null : t; } }
        public static bool On { get { return Root != null; } }

        // name, id, store, two colours, size in GB, minutes in all, days since last played (-1 = never)
        static readonly object[][] Set = {
            new object[] { "Delta Shift", "steam:2507950", "steam", "#0F3B2E", "#38E0A0", 96, 7740, 0 },
            new object[] { "Starfall Rangers", "steam:3100010", "steam", "#1B1F4A", "#7A8CFF", 62, 12560, 1 },
            new object[] { "Starfall Rangers", "epic:StarfallRangers", "epic", "#1B1F4A", "#7A8CFF", 61, 0, -1 },
            new object[] { "Lethal Orbit", "steam:1966720", "steam", "#2A0F12", "#F05A5A", 1, 5410, 4 },
            new object[] { "Hollow Pines", "steam:3100020", "steam", "#13261A", "#9BD86A", 18, 2210, 13 },
            new object[] { "Pixel Kart Rally", "steam:3100030", "steam", "#3A1240", "#F0A0E8", 9, 640, 27 },
            new object[] { "Frostbite Outpost", "battlenet:Frostbite", "battlenet", "#0E2236", "#8FD0F5", 74, 30880, 40 },
            new object[] { "Clockwork Harbor", "steam:3100040", "steam", "#33230E", "#F0BE5A", 23, 0, -1 },
            new object[] { "Moonlit Market", "epic:MoonlitMarket", "epic", "#1E1430", "#C9B2F5", 4, 300, 90 },
            new object[] { "Tidecaller", "steam:3100050", "steam", "#0B2C33", "#5AD2E0", 41, 1480, 60 },
            new object[] { "Ember Road", "steam:3100060", "steam", "#38140A", "#FF8A3C", 33, 90, 200 },
            new object[] { "Quiet Signal", "custom:quiet-signal", "custom", "#202028", "#B9AFCB", 2, 45, 300 },
        };

        public static List<Game> Games()
        {
            var art = Path.Combine(Root, "HallowHub", "testart"); Directory.CreateDirectory(art);
            var list = new List<Game>(); int i = 0;
            foreach (var r in Set)
            {
                string name = (string)r[0], id = (string)r[1], src = (string)r[2], c1 = (string)r[3], c2 = (string)r[4];
                var key = id.Replace(':', '_');
                var g = new Game { Id = id, Name = name, Source = src, InstallDir = Path.Combine(Root, "Games", name), Launch = src == "custom" ? Path.Combine(Root, "Games", name, "game.exe") : "", Args = "" };
                g.SizeBytes = (long)((int)r[5] * (1L << 30)); g.Drive = i % 3 == 0 ? "D:" : "C:";
                g.UpdatedAt = DateTime.Today.AddDays(-i * 5);
                if (src == "steam")
                {
                    g.SteamMinutes = (int)r[6]; g.LauncherLast = (int)r[7] >= 0 ? DateTime.Now.AddDays(-(int)r[7]).AddHours(-3) : (DateTime?)null;
                    g.Cover = Draw(Path.Combine(art, key + "-cover.png"), 600, 900, name, c1, c2, i, "cover");
                    g.Hero = Draw(Path.Combine(art, key + "-hero.png"), 1920, 620, name, c1, c2, i, "hero");
                    g.Header = Draw(Path.Combine(art, key + "-header2.png"), 920, 430, name, c1, c2, i, "header");
                    g.Logo = Draw(Path.Combine(art, key + "-logo.png"), 900, 300, name, c1, c2, i, "logo");
                }
                else if (src == "epic" && name == "Moonlit Market")
                {
                    g.Cover = Draw(Path.Combine(art, key + "-cover.png"), 600, 900, name, c1, c2, i, "cover");
                    g.Hero = Draw(Path.Combine(art, key + "-hero.png"), 1920, 620, name, c1, c2, i, "hero");
                }
                if (name == "Tidecaller") { g.Updating = true; g.Progress = 0.42; }
                if (name == "Hollow Pines") g.NeedsUpdate = true;
                list.Add(g); i++;
            }
            return list;
        }

        // the made-up playtime, favourites and a collection (only when the test folder has no hub.json of its own)
        public static HubSettings Settings()
        {
            var s = new HubSettings();
            var rnd = new Random(28);
            foreach (var r in Set)
            {
                int total = (int)r[6], ago = (int)r[7]; if (total <= 0 || ago < 0) continue;
                var d = s.For((string)r[1]);
                // most of it long ago, some in the last two weeks
                d.Minutes = total * 0.8; int left = total - (int)d.Minutes;
                for (int k = 0; left > 0 && k < 12; k++) { int m = Math.Min(left, 30 + rnd.Next(120)); var day = DateTime.Today.AddDays(-(ago + k * 2)); d.AddMinutes(m, day.AddHours(20)); left -= m; }
                d.LastPlayed = DateTime.Now.AddDays(-ago).AddHours(-2).ToString("o"); d.Sessions = 3 + rnd.Next(40);
            }
            s.For("steam:2507950").Favorite = true; s.For("steam:3100010").Favorite = true; s.For("steam:1966720").Favorite = true;
            s.Collections.Add("With friends");
            foreach (var id in new[] { "steam:1966720", "steam:3100030", "steam:2507950" }) s.For(id).Tags.Add("With friends");
            s.Selected = "steam:2507950";
            return s;
        }

        // the four kinds of picture a launcher gives us, drawn from two colours: a sky, hills, a moon and (cover / header) the name
        static string Draw(string path, int w, int h, string name, string c1, string c2, int seed, string kind)
        {
            if (File.Exists(path)) return path;
            try
            {
                Color a = (Color)ColorConverter.ConvertFromString(c1), b = (Color)ColorConverter.ConvertFromString(c2);
                var rnd = new Random(seed * 31 + kind.Length);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    if (kind != "logo")
                    {
                        var sky = new LinearGradientBrush(Mix(a, Colors.Black, 0.35), Mix(a, b, 0.55), 90); dc.DrawRectangle(sky, null, new Rect(0, 0, w, h));
                        var glow = new RadialGradientBrush(Color.FromArgb(170, b.R, b.G, b.B), Color.FromArgb(0, b.R, b.G, b.B));
                        double mx = w * (0.55 + rnd.NextDouble() * 0.3), my = h * (0.25 + rnd.NextDouble() * 0.15), mr = Math.Min(w, h) * 0.22;
                        dc.DrawEllipse(glow, null, new Point(mx, my), mr * 2.4, mr * 2.4);
                        dc.DrawEllipse(new SolidColorBrush(Mix(b, Colors.White, 0.45)), null, new Point(mx, my), mr * 0.55, mr * 0.55);
                        for (int layer = 0; layer < 3; layer++)
                        {
                            var geo = new StreamGeometry();
                            using (var g = geo.Open())
                            {
                                double baseY = h * (0.58 + layer * 0.13); g.BeginFigure(new Point(0, h), true, true); g.LineTo(new Point(0, baseY), true, false);
                                int steps = 7 + layer * 2; for (int s = 1; s <= steps; s++) g.LineTo(new Point(w * s / (double)steps, baseY - rnd.NextDouble() * h * (0.16 - layer * 0.03)), true, false);
                                g.LineTo(new Point(w, h), true, false);
                            }
                            geo.Freeze(); dc.DrawGeometry(new SolidColorBrush(Mix(a, Colors.Black, 0.25 + layer * 0.22)), null, geo);
                        }
                    }
                    if (kind == "cover" || kind == "header" || kind == "logo")
                    {
                        var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal);
                        double size = kind == "cover" ? w * 0.13 : kind == "header" ? h * 0.11 : h * 0.30;
                        var ft = new FormattedText(name.ToUpperInvariant(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, Brushes.White, 1.0)
                        { MaxTextWidth = kind == "header" ? w * 0.56 : w * 0.86, TextAlignment = kind == "logo" ? TextAlignment.Left : TextAlignment.Center };
                        double x = kind == "logo" ? 0 : kind == "header" ? w * 0.22 : w * 0.07, y = kind == "cover" ? h * 0.12 : kind == "header" ? h * 0.30 : (h - ft.Height) / 2;
                        if (kind != "logo") dc.DrawText(new FormattedText(name.ToUpperInvariant(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 1.0) { MaxTextWidth = ft.MaxTextWidth, TextAlignment = TextAlignment.Center }, new Point(x + 3, y + 4));
                        dc.DrawText(ft, new Point(x, y));
                        if (kind == "logo") dc.DrawRectangle(new SolidColorBrush(b), null, new Rect(0, y + ft.Height + 6, Math.Min(ft.WidthIncludingTrailingWhitespace, w) * 0.4, 8));
                    }
                }
                var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); bmp.Render(dv);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var f = File.Create(path)) enc.Save(f);
                return path;
            }
            catch { return null; }
        }
        static Color Mix(Color x, Color y, double k) { return Color.FromRgb((byte)(x.R + (y.R - x.R) * k), (byte)(x.G + (y.G - x.G) * k), (byte)(x.B + (y.B - x.B) * k)); }
    }
}
