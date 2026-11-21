// Hallow Hub v2.8 - game details for Steam games (description, genres, features, developer, publisher, release date,
// Metacritic, screenshots) from Steam's public store data. Fetched for the game you open, and one game at a time while
// Hub is idle; kept for 30 days. Nothing is crawled in bulk.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowHub
{
    public class GameMeta
    {
        public string About = "", Developer = "", Publisher = "", Released = "";
        public int Metacritic;
        public List<string> Genres = new List<string>(), Features = new List<string>(), Shots = new List<string>();
        public IEnumerable<string> Tags { get { return Genres.Concat(Features); } }
    }

    public static class SteamMeta
    {
        // the store's categories worth filtering by (the rest are audio / accessibility details)
        static readonly HashSet<string> Useful = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Single-player", "Multi-player", "Co-op", "Online Co-op", "LAN Co-op", "Shared/Split Screen Co-op", "Shared/Split Screen", "PvP", "Online PvP", "LAN PvP",
            "Shared/Split Screen PvP", "Cross-Platform Multiplayer", "MMO", "Full controller support", "Partial Controller Support", "VR Supported", "VR Only",
            "Steam Workshop", "Steam Achievements", "Steam Cloud", "Remote Play Together", "In-App Purchases", "Includes level editor" };
        public static string Dir
        {
            get { var t = Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST"); return Path.Combine(string.IsNullOrEmpty(t) ? HubSettings.Dir : Path.Combine(t, "HallowHub"), "meta"); }
        }
        static string FileFor(string id) { return Path.Combine(Dir, id + ".json"); }
        public static string ShotDir(string id) { return Path.Combine(Dir, id); }
        public static bool Fresh(string id) { try { var f = FileFor(id); return File.Exists(f) && (DateTime.Now - File.GetLastWriteTime(f)).TotalDays < 30; } catch { return false; } }
        public static bool Have(string id) { try { return File.Exists(FileFor(id)); } catch { return false; } }

        public static GameMeta Cached(string id)
        {
            try { var f = FileFor(id); return File.Exists(f) ? Parse(File.ReadAllText(f, Encoding.UTF8), id) : null; } catch { return null; }
        }
        // from Steam (blocks: off the window's thread). Also saves up to 4 screenshot thumbnails next to it.
        public static GameMeta Fetch(string id)
        {
            if (!Regex.IsMatch(id ?? "", "^[0-9]{1,10}$")) return null;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var json = Get("https://store.steampowered.com/api/appdetails?appids=" + id + "&l=english");
                if (json == null) return null;
                var m = Parse(json, id);
                Directory.CreateDirectory(Dir);
                var tmp = FileFor(id) + ".tmp"; File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(FileFor(id))) File.Replace(tmp, FileFor(id), null); else File.Move(tmp, FileFor(id));
                if (m != null)
                {
                    Directory.CreateDirectory(ShotDir(id));
                    for (int i = 0; i < m.Shots.Count && i < 4; i++)
                    {
                        var u = m.Shots[i]; var f = Path.Combine(ShotDir(id), "shot" + i + ".jpg");
                        if (File.Exists(f) || !IsSteamImage(u)) continue;
                        try { using (var wc = new WebClient()) { wc.Headers[HttpRequestHeader.UserAgent] = "HallowHub/" + Program.Version; wc.DownloadFile(u, f + ".tmp"); } File.Move(f + ".tmp", f); } catch { try { File.Delete(f + ".tmp"); } catch { } }
                        Thread.Sleep(250);
                    }
                }
                return m;
            }
            catch { return null; }
        }
        static bool IsSteamImage(string u)
        {
            Uri x; return Uri.TryCreate(u, UriKind.Absolute, out x) && x.Scheme == "https" && (x.Host.EndsWith(".steamstatic.com") || x.Host.EndsWith(".akamaihd.net") || x.Host.EndsWith(".steampowered.com"));
        }
        static string Get(string url)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url); req.Timeout = 10000; req.ReadWriteTimeout = 10000; req.UserAgent = "HallowHub/" + Program.Version;
                using (var r = req.GetResponse()) using (var s = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) return s.ReadToEnd();
            }
            catch { return null; }
        }
        public static GameMeta Parse(string json, string id)
        {
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(json);
                object e; if (root == null || !root.TryGetValue(id, out e)) return null;
                var entry = e as Dictionary<string, object>; if (entry == null || !Family.Bool(entry, "success")) return new GameMeta();   // not on the store any more: remember that
                object dd; entry.TryGetValue("data", out dd); var d = dd as Dictionary<string, object>; if (d == null) return new GameMeta();
                var m = new GameMeta();
                m.About = SteamNews.Plain(Family.Str(d, "short_description") ?? "");
                Func<string, List<string>> strings = k => { var r = new List<string>(); object o; if (d.TryGetValue(k, out o) && o is System.Collections.ArrayList) foreach (var x in (System.Collections.ArrayList)o) if (x != null) r.Add(x.ToString()); return r; };
                m.Developer = string.Join(", ", strings("developers")); m.Publisher = string.Join(", ", strings("publishers"));
                object rd; if (d.TryGetValue("release_date", out rd)) m.Released = Family.Str(rd as Dictionary<string, object>, "date") ?? "";
                object mc; if (d.TryGetValue("metacritic", out mc)) m.Metacritic = (int)Family.Num(mc as Dictionary<string, object>, "score");
                m.Genres = Family.List(d, "genres").Select(x => Family.Str(x, "description")).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
                m.Features = Family.List(d, "categories").Select(x => Family.Str(x, "description")).Where(x => x != null && Useful.Contains(x)).Distinct().ToList();
                m.Shots = Family.List(d, "screenshots").Select(x => Family.Str(x, "path_thumbnail")).Where(x => !string.IsNullOrEmpty(x)).Take(6).ToList();
                return m;
            }
            catch { return null; }
        }
    }

    public partial class HubWindow
    {
        Button aboutBtn; Border aboutPanel; StackPanel aboutBody; TextBlock aboutTitle; DispatcherTimer metaSoon; int idleTicks;
        readonly Dictionary<string, GameMeta> metas = new Dictionary<string, GameMeta>(); readonly HashSet<string> metaBusy = new HashSet<string>();

        // what Hub knows about a game (from the cache), or null
        GameMeta MetaOf(Game g)
        {
            var id = SteamId(g); if (id == null) return null;
            GameMeta m; if (metas.TryGetValue(id, out m)) return m;
            m = SteamMeta.Cached(id); if (m != null) metas[id] = m;
            return m;
        }
        IEnumerable<string> TagsOf(Game g) { var m = MetaOf(g); return m == null ? Enumerable.Empty<string>() : m.Tags; }

        void FetchMeta(string id, Action after)
        {
            if (id == null || Snap.On || metaBusy.Contains(id)) return;
            metaBusy.Add(id);
            new Thread(() =>
            {
                var m = SteamMeta.Fetch(id);
                Dispatcher.BeginInvoke(new Action(() => { metaBusy.Remove(id); if (m != null) metas[id] = m; if (after != null) after(); }));
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }
        // ShowHero: the About button, and the details fetched a moment after you stop on a game
        void ShowAboutButton(Game g)
        {
            if (aboutBtn == null) return;
            var id = SteamId(g);
            aboutBtn.Visibility = id == null ? Visibility.Collapsed : Visibility.Visible;
            if (id == null) return;
            var m = MetaOf(g);
            aboutBtn.ToolTip = m != null && m.About.Length > 0 ? m.About + (m.Genres.Count > 0 ? "\n" + string.Join(", ", m.Genres) : "") : "About this game: description, genres, screenshots";
            if (Snap.On || SteamMeta.Fresh(id)) return;
            if (metaSoon == null) { metaSoon = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; metaSoon.Tick += (s, e) => { metaSoon.Stop(); var sid = SteamId(sel); FetchMeta(sid, () => { if (SteamId(sel) == sid) { ShowAboutButton(sel); if (aboutPanel.Visibility == Visibility.Visible) FillAbout(); } RebuildFilterChoicesIfOpen(); }); }; }
            metaSoon.Stop(); metaSoon.Start();
        }
        // while nothing is running: one game without details a minute, so genre filters fill in
        void IdleMeta()
        {
            if (Snap.On || tracker.Now.Count > 0 || ++idleTicks % 4 != 0) return;
            var g = games.FirstOrDefault(x => SteamId(x) != null && !SteamMeta.Have(SteamId(x)) && !metaBusy.Contains(SteamId(x)));
            if (g != null) FetchMeta(SteamId(g), RebuildFilterChoicesIfOpen);
        }
        void RebuildFilterChoicesIfOpen() { if (filterPop != null && filterPop.IsOpen) FillFilters(); }

        Border BuildAbout()
        {
            var b = new Border { Background = B("#211B2A"), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24, 16, 12, 18), Width = 800, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 62, 0, 24), Visibility = Visibility.Collapsed };
            b.Effect = HallowLook.Shadow(30, 0.6, 4);
            var dp = new DockPanel();
            var sh = new DockPanel { Margin = new Thickness(0, 0, 12, 12) };
            var close = new Button { Content = "x", Tag = "icon", Style = (Style)FindResource("Btn"), Width = 30, Height = 28, Padding = new Thickness(0), ToolTip = "Close" };
            close.Click += (s, e) => aboutPanel.Visibility = Visibility.Collapsed;
            DockPanel.SetDock(close, Dock.Right); sh.Children.Add(close);
            var store = new Button { Content = "Store page", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0) };
            store.Click += (s, e) => { var id = SteamId(sel); if (id != null) OpenUrl("steam://store/" + id); };
            DockPanel.SetDock(store, Dock.Right); sh.Children.Add(store);
            var ic = Icons.Make("circle-info", 16, B("#F07820")); ic.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(ic, Dock.Left); sh.Children.Add(ic);
            aboutTitle = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 17, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            sh.Children.Add(aboutTitle);
            DockPanel.SetDock(sh, Dock.Top); dp.Children.Add(sh);
            aboutBody = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            dp.Children.Add(new ScrollViewer { Content = aboutBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 620 });
            b.Child = dp; return b;
        }
        void OpenAbout()
        {
            if (sel == null) return;
            statsBtn.IsChecked = false; storageBtn.IsChecked = false; gearBtn.IsChecked = false; newsPanel.Visibility = Visibility.Collapsed; wishPanel.Visibility = Visibility.Collapsed;
            aboutPanel.Visibility = Visibility.Visible; FillAbout();
            var id = SteamId(sel); if (id != null && !SteamMeta.Fresh(id)) FetchMeta(id, () => { if (aboutPanel.Visibility == Visibility.Visible) FillAbout(); ShowAboutButton(sel); });
        }
        void FillAbout()
        {
            aboutBody.Children.Clear();
            var g = sel; var id = SteamId(g); if (id == null) { aboutPanel.Visibility = Visibility.Collapsed; return; }
            aboutTitle.Text = g.Name;
            var m = MetaOf(g);
            if (m == null) { aboutBody.Children.Add(new TextBlock { Text = metaBusy.Contains(id) ? "Getting the details from Steam..." : "No details yet - Hub asks Steam when you open a game.", Foreground = (Brush)FindResource("Muted") }); return; }
            if (m.About.Length == 0 && m.Genres.Count == 0) { aboutBody.Children.Add(new TextBlock { Text = "Steam's store has no details for this one.", Foreground = (Brush)FindResource("Muted") }); return; }
            if (m.About.Length > 0) aboutBody.Children.Add(new TextBlock { Text = m.About, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 20, Margin = new Thickness(0, 0, 0, 12) });
            var facts = new List<string>();
            if (m.Developer.Length > 0) facts.Add("Developer: " + m.Developer);
            if (m.Publisher.Length > 0 && m.Publisher != m.Developer) facts.Add("Publisher: " + m.Publisher);
            if (m.Released.Length > 0) facts.Add("Released: " + m.Released);
            if (m.Metacritic > 0) facts.Add("Metacritic: " + m.Metacritic);
            if (facts.Count > 0) aboutBody.Children.Add(new TextBlock { Text = string.Join("   ·   ", facts), Foreground = B("#CFC8DC"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            Action<string, string, List<string>> chips = (label, icon, list) =>
            {
                if (list.Count == 0) return;
                aboutBody.Children.Add(SectionLabel(label, icon));
                var wp = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                foreach (var t in list)
                {
                    var tag = t;
                    var c = new Button { Content = t, Style = (Style)FindResource("Btn"), Padding = new Thickness(10, 3, 10, 4), FontSize = 11.5, Margin = new Thickness(0, 0, 6, 6), ToolTip = "Show only games with " + t };
                    c.Click += (s, e) => { S.FTags = new List<string> { tag }; S.Save(); aboutPanel.Visibility = Visibility.Collapsed; RebuildGrid(); Status("Showing " + tag + " games - Filters to change it."); };
                    wp.Children.Add(c);
                }
                aboutBody.Children.Add(wp);
            };
            chips("GENRES", "tag", m.Genres); chips("FEATURES", "gamepad", m.Features);
            var shots = Directory.Exists(SteamMeta.ShotDir(id)) ? Directory.GetFiles(SteamMeta.ShotDir(id), "shot*.jpg").OrderBy(x => x).Take(4).ToList() : new List<string>();
            if (shots.Count > 0)
            {
                aboutBody.Children.Add(SectionLabel("SCREENSHOTS", "image"));
                var wp = new WrapPanel();
                foreach (var f in shots)
                {
                    var img = Img(f, 400); if (img == null) continue;
                    var file = f;
                    var t = new Border { Width = 176, Height = 99, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill }, ToolTip = "Open it bigger" };
                    t.MouseLeftButtonUp += (s, e) => OpenUrl(file);
                    wp.Children.Add(t);
                }
                aboutBody.Children.Add(wp);
            }
            aboutBody.Children.Add(new TextBlock { Text = "From Steam's public store page - kept for a month.", Foreground = (Brush)FindResource("Muted"), FontSize = 11, Margin = new Thickness(2, 4, 0, 0) });
        }
    }
}
