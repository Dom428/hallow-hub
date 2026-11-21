// Hallow Hub v2.6 - patch notes and news for Steam games, from Steam's public news API (no key, no sign-in).
// Fetched only when you open a game's page, kept 6 hours per game, shown as plain text (no remote images); Read more opens the browser.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowHub
{
    public class NewsItem { public string Title, Url, Text, Feed; public DateTime When; }

    public static class SteamNews
    {
        static string Dir
        {
            get
            {
                var t = Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST");
                return Path.Combine(string.IsNullOrEmpty(t) ? HubSettings.Dir : Path.Combine(t, "HallowHub"), "news");
            }
        }
        static string FileFor(string appid) { return Path.Combine(Dir, appid + ".json"); }
        public static bool Fresh(string appid) { try { var f = FileFor(appid); return File.Exists(f) && (DateTime.Now - File.GetLastWriteTime(f)).TotalHours < 6; } catch { return false; } }

        // what's in the cache (any age), or null
        public static List<NewsItem> Cached(string appid)
        {
            try { var f = FileFor(appid); return File.Exists(f) ? Parse(File.ReadAllText(f, Encoding.UTF8)) : null; } catch { return null; }
        }
        // from Steam (blocks: call it off the window's thread); null when it can't be reached
        public static List<NewsItem> Fetch(string appid)
        {
            if (!Regex.IsMatch(appid ?? "", "^[0-9]{1,10}$")) return null;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create("https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=" + appid + "&count=5&maxlength=600&format=json");
                req.Timeout = 8000; req.ReadWriteTimeout = 8000; req.UserAgent = "HallowHub/" + Program.Version;
                string json;
                using (var r = req.GetResponse()) using (var s = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) json = s.ReadToEnd();
                var items = Parse(json); if (items == null) return null;
                Directory.CreateDirectory(Dir);
                var tmp = FileFor(appid) + ".tmp"; File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(FileFor(appid))) File.Replace(tmp, FileFor(appid), null); else File.Move(tmp, FileFor(appid));
                return items;
            }
            catch { return null; }
        }
        public static List<NewsItem> Parse(string json)
        {
            try
            {
                var js = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
                var root = js.Deserialize<Dictionary<string, object>>(json);
                object an; if (root == null || !root.TryGetValue("appnews", out an)) return null;
                var list = Family.List(an as Dictionary<string, object>, "newsitems");
                var r = new List<NewsItem>();
                foreach (var n in list.Take(5))
                {
                    var url = Family.Str(n, "url") ?? "";
                    if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) url = null; else url = url.Replace(" ", "%20");   // only real web links open
                    r.Add(new NewsItem { Title = Plain(Family.Str(n, "title") ?? "News"), Url = url, Text = Plain(Family.Str(n, "contents") ?? ""), Feed = Plain(Family.Str(n, "feedlabel") ?? ""),
                        When = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(Family.Num(n, "date")).ToLocalTime() });
                }
                return r;
            }
            catch { return null; }
        }
        // BBCode and HTML out, entities decoded, Steam's image placeholders and bare image links dropped, whitespace tidied
        public static string Plain(string s)
        {
            s = Regex.Replace(s, @"\{STEAM_CLAN_IMAGE\}\S*", " ");
            s = Regex.Replace(s, @"\[img\].*?\[/img\]", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            s = Regex.Replace(s, @"\[(previewyoutube|video)[^\]]*\].*?\[/\1\]", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            s = Regex.Replace(s, @"\[\*\]", "\n• ");
            s = Regex.Replace(s, @"\[/?(h[1-6]|p|list|olist|tr|table)\b[^\]]*\]", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\[/?[a-z0-9]+(=[^\]]*)?\]", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<\s*(br|/p|/h[1-6]|/li|/div)[^>]*>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<li[^>]*>", "\n• ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<[^>]+>", "");
            s = WebUtility.HtmlDecode(s);
            s = Regex.Replace(s, @"https?://\S+\.(png|jpe?g|gif|webp)\S*", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"[ \t ]+", " ");
            s = Regex.Replace(s, @" *\n[ \n]*", "\n");
            s = Regex.Replace(s, @"\n{2,}", "\n");
            return s.Trim();
        }
    }

    public partial class HubWindow
    {
        Button newsBtn; Border newsPanel; StackPanel newsBody; DispatcherTimer newsSoon; string newsFor; readonly HashSet<string> newsBusy = new HashSet<string>();

        // the Steam app id of a game (or of its Steam copy), or null
        static string SteamId(Game g)
        {
            if (g == null) return null;
            var c = new[] { g }.Concat(g.Alts).FirstOrDefault(x => x.Source == "steam" && x.Id.StartsWith("steam:"));
            return c == null ? null : c.Id.Substring(6);
        }

        Border BuildNews()
        {
            var b = new Border { Background = B("#211B2A"), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24, 16, 12, 18), Width = 760, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 62, 0, 24), Visibility = Visibility.Collapsed };
            b.Effect = HallowLook.Shadow(30, 0.6, 4);
            var dp = new DockPanel();
            var sh = new DockPanel { Margin = new Thickness(0, 0, 12, 12) };
            var close = new Button { Content = "x", Tag = "icon", Style = (Style)FindResource("Btn"), Width = 30, Height = 28, Padding = new Thickness(0), ToolTip = "Close" };
            close.Click += (s, e) => newsPanel.Visibility = Visibility.Collapsed;
            DockPanel.SetDock(close, Dock.Right); sh.Children.Add(close);
            var ic = Icons.Make("newspaper", 16, B("#F07820")); ic.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(ic, Dock.Left); sh.Children.Add(ic);
            newsTitle = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 17, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            sh.Children.Add(newsTitle);
            DockPanel.SetDock(sh, Dock.Top); dp.Children.Add(sh);
            newsBody = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            dp.Children.Add(new ScrollViewer { Content = newsBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 600 });
            b.Child = dp; return b;
        }
        TextBlock newsTitle;

        // called from ShowHero: a Steam game gets the News button; its news is fetched a moment after you stop on it
        void ShowNewsButton(Game g)
        {
            if (newsBtn == null) return;
            var id = SteamId(g);
            newsBtn.Visibility = id == null ? Visibility.Collapsed : Visibility.Visible;
            if (id == null) return;
            var have = SteamNews.Cached(id);
            newsBtn.ToolTip = have != null && have.Count > 0 ? "News and patch notes - newest: " + have[0].Title + " (" + HubSettings.Ago(have[0].When.ToUniversalTime()).ToLowerInvariant() + ")" : "News and patch notes from Steam";
            if (Snap.On || SteamNews.Fresh(id) || newsBusy.Contains(id)) return;
            if (newsSoon == null) { newsSoon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) }; newsSoon.Tick += (s, e) => { newsSoon.Stop(); FetchNews(SteamId(sel), false); }; }
            newsSoon.Stop(); newsSoon.Start();   // not while you arrow through the library
        }
        void FetchNews(string id, bool show)
        {
            if (id == null || newsBusy.Contains(id) || SteamNews.Fresh(id)) { if (show) FillNews(); return; }
            newsBusy.Add(id);
            new Thread(() =>
            {
                var r = SteamNews.Fetch(id);
                Dispatcher.BeginInvoke(new Action(() => { newsBusy.Remove(id); if (SteamId(sel) == id) { ShowNewsButton(sel); if (newsPanel.Visibility == Visibility.Visible) FillNews(); } if (r == null && show) Status("Couldn't reach Steam for news - try again later."); }));
            }) { IsBackground = true }.Start();
        }
        void OpenNews()
        {
            if (sel == null) return;
            statsBtn.IsChecked = false; storageBtn.IsChecked = false; gearBtn.IsChecked = false;
            newsPanel.Visibility = Visibility.Visible; FillNews();
            FetchNews(SteamId(sel), true);
        }
        void FillNews()
        {
            newsBody.Children.Clear();
            var g = sel; var id = SteamId(g); if (id == null) { newsPanel.Visibility = Visibility.Collapsed; return; }
            newsTitle.Text = g.Name + " - news and patch notes";
            var items = SteamNews.Cached(id);
            if (items == null) { newsBody.Children.Add(new TextBlock { Text = newsBusy.Contains(id) ? "Getting the news from Steam..." : "No news yet - Hub asks Steam when you open a game.", Foreground = (Brush)FindResource("Muted") }); return; }
            if (items.Count == 0) { newsBody.Children.Add(new TextBlock { Text = "Steam has no news for this game.", Foreground = (Brush)FindResource("Muted") }); return; }
            foreach (var n in items)
            {
                var card = new Border { Background = B("#2A2333"), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 10) };
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = n.When.ToString("d MMM yyyy") + (n.Feed.Length > 0 ? "   ·   " + n.Feed : ""), Foreground = B(HallowLook.Light ? "#C9B2F5" : "#A98BE0"), FontSize = 11, FontWeight = FontWeights.SemiBold });
                sp.Children.Add(new TextBlock { Text = n.Title, FontSize = 15, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 6) });
                var text = n.Text.Length > 600 ? n.Text.Substring(0, 600).TrimEnd() + "..." : n.Text;
                if (text.Length > 0) sp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = B("#CFC8DC"), LineHeight = 18 });
                if (n.Url != null)
                {
                    var more = new Button { Content = "Read more", Style = (Style)FindResource("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(10, 4, 10, 4), FontSize = 11.5, ToolTip = n.Url };
                    var u = n.Url; more.Click += (s, e) => OpenUrl(u);
                    sp.Children.Add(more);
                }
                card.Child = sp; newsBody.Children.Add(card);
            }
            newsBody.Children.Add(new TextBlock { Text = "From Steam's public news - checked at most every 6 hours, when you open the game.", Foreground = (Brush)FindResource("Muted"), FontSize = 11, Margin = new Thickness(2, 2, 0, 0) });
        }
    }
}
