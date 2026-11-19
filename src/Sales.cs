// Hallow Hub v2.7 - sale alerts for your Steam wishlist (off unless you turn them on). Uses only Steam's public, keyless
// endpoints: IWishlistService/GetWishlist (a public wishlist) and the store's appdetails (prices). Checked once a day;
// tells you through Hub's tray icon when a wishlisted game is discounted or drops below a price you set.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WF = System.Windows.Forms;

namespace HallowHub
{
    [DataContract]
    public class WishItem
    {
        [DataMember] public string AppId = "", Name = "", Currency = "", Price = "";   // Price: Steam's own formatted final price
        [DataMember] public int Final, Initial, Discount;   // cents
        [DataMember] public bool Free, NoPrice;
    }
    [DataContract]
    public class WishCache
    {
        [DataMember] public string Checked = "", SteamId = "";
        [DataMember] public List<WishItem> Items = new List<WishItem>();
        [DataMember] public Dictionary<string, string> Names = new Dictionary<string, string>();   // appid -> name, kept forever
    }

    public static class Wishlist
    {
        static string Dir
        {
            get { var t = Environment.GetEnvironmentVariable("HALLOW_FAMILY_TEST"); return string.IsNullOrEmpty(t) ? HubSettings.Dir : Path.Combine(t, "HallowHub"); }
        }
        static string FilePath { get { return Path.Combine(Dir, "wishlist.json"); } }
        static DataContractJsonSerializerSettings Opts() { return new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }; }
        public static WishCache Load()
        {
            try { if (File.Exists(FilePath)) using (var f = File.OpenRead(FilePath)) { var c = (WishCache)new DataContractJsonSerializer(typeof(WishCache), Opts()).ReadObject(f); if (c != null) return c; } } catch { }
            return new WishCache();
        }
        public static void Save(WishCache c)
        {
            if (HubSettings.Frozen) return;
            try
            {
                Directory.CreateDirectory(Dir); var tmp = FilePath + ".tmp";
                using (var f = File.Create(tmp)) new DataContractJsonSerializer(typeof(WishCache), Opts()).WriteObject(f, c);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
            }
            catch { }
        }

        // SteamID64 from what you typed: the number, a /profiles/<id> link, or a /id/<name> link (looked up on your public profile)
        public static string SteamId(string input, out string error)
        {
            error = null; var s = (input ?? "").Trim();
            var m = Regex.Match(s, @"(?:^|/profiles/)(7656119\d{10})(?:/|$)");
            if (m.Success) return m.Groups[1].Value;
            m = Regex.Match(s, @"steamcommunity\.com/id/([A-Za-z0-9_-]{2,32})", RegexOptions.IgnoreCase);
            if (!m.Success) { error = "Type your SteamID64 (17 digits starting 7656119) or your Steam profile link."; return null; }
            var xml = Get("https://steamcommunity.com/id/" + m.Groups[1].Value + "/?xml=1");
            var id = xml == null ? null : IdFromProfileXml(xml);
            if (id == null) error = "Couldn't find that Steam profile (is it public?).";
            return id;
        }
        public static string IdFromProfileXml(string xml) { var m = Regex.Match(xml ?? "", @"<steamID64>\s*(7656119\d{10})\s*</steamID64>"); return m.Success ? m.Groups[1].Value : null; }

        static string Get(string url)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create(url); req.Timeout = 10000; req.ReadWriteTimeout = 10000; req.UserAgent = "HallowHub/" + Program.Version;
                using (var r = req.GetResponse()) using (var s = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) return s.ReadToEnd();
            }
            catch { return null; }
        }

        public static List<string> ParseWishlist(string json)
        {
            var r = new List<string>();
            try
            {
                var d = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(json);
                object resp; if (d == null || !d.TryGetValue("response", out resp)) return null;
                foreach (var it in Family.List(resp as Dictionary<string, object>, "items")) { var a = Family.Str(it, "appid"); if (a != null && Regex.IsMatch(a, "^[0-9]+$")) r.Add(a); }
                return r;
            }
            catch { return null; }
        }
        // appdetails with filters=price_overview: { "<appid>": { success, data: { price_overview: {...} } or [] } }
        public static void ParsePrices(string json, Dictionary<string, WishItem> into)
        {
            try
            {
                var d = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(json);
                foreach (var kv in d)
                {
                    WishItem w; if (!into.TryGetValue(kv.Key, out w)) continue;
                    var e = kv.Value as Dictionary<string, object>; if (e == null) continue;
                    object data; e.TryGetValue("data", out data);
                    var po = data as Dictionary<string, object>; object p = null;
                    if (po != null) po.TryGetValue("price_overview", out p);
                    var pr = p as Dictionary<string, object>;
                    if (pr == null) { w.NoPrice = !Family.Bool(e, "success"); w.Free = Family.Bool(e, "success"); continue; }
                    w.Currency = Family.Str(pr, "currency") ?? ""; w.Final = (int)Family.Num(pr, "final"); w.Initial = (int)Family.Num(pr, "initial"); w.Discount = (int)Family.Num(pr, "discount_percent");
                    w.Price = Family.Str(pr, "final_formatted") ?? "";
                }
            }
            catch { }
        }
        public static string NameFromDetails(string json, string appid)
        {
            try
            {
                var d = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(json);
                object e; if (!d.TryGetValue(appid, out e)) return null;
                object data; if (!((Dictionary<string, object>)e).TryGetValue("data", out data)) return null;
                return Family.Str(data as Dictionary<string, object>, "name");
            }
            catch { return null; }
        }

        // the daily check (blocks: off the window's thread). A handful of requests: the wishlist, prices 50 at a time, a few names.
        public static WishCache Check(string steamId, WishCache old, out string error)
        {
            error = null;
            var wl = Get("https://api.steampowered.com/IWishlistService/GetWishlist/v1/?steamid=" + steamId);
            var ids = wl == null ? null : ParseWishlist(wl);
            if (ids == null) { error = "Couldn't reach Steam for your wishlist - Hub tries again later."; return null; }
            var c = new WishCache { Checked = DateTime.Now.ToString("o"), SteamId = steamId, Names = old.Names ?? new Dictionary<string, string>() };
            var items = ids.Distinct().ToDictionary(x => x, x => new WishItem { AppId = x });
            foreach (var chunk in ids.Distinct().Select((x, i) => new { x, i }).GroupBy(x => x.i / 50).Select(g => g.Select(y => y.x).ToList()))
            {
                var pj = Get("https://store.steampowered.com/api/appdetails?filters=price_overview&appids=" + string.Join(",", chunk));
                if (pj != null) ParsePrices(pj, items);
                Thread.Sleep(400);
            }
            // names: what's on sale first, then the rest, at most 25 new ones a day (Steam only gives names one game at a time)
            int asked = 0;
            foreach (var w in items.Values.OrderByDescending(x => x.Discount).ThenBy(x => x.AppId))
            {
                string n;
                if (c.Names.TryGetValue(w.AppId, out n)) { w.Name = n; continue; }
                if (asked >= 25) continue;
                asked++; var dj = Get("https://store.steampowered.com/api/appdetails?filters=basic&appids=" + w.AppId);
                n = dj == null ? null : NameFromDetails(dj, w.AppId);
                if (!string.IsNullOrEmpty(n)) { c.Names[w.AppId] = n; w.Name = n; }
                Thread.Sleep(400);
            }
            c.Items = items.Values.ToList();
            return c;
        }
    }

    public partial class HubWindow
    {
        Button wishBtn; Border wishPanel; StackPanel wishBody; bool wishChecking;

        static string WishName(WishItem w) { return string.IsNullOrEmpty(w.Name) ? "Steam app " + w.AppId : w.Name; }
        static string Money(int cents, string cur) { return (cents / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(cur) ? "" : " " + cur); }
        bool Alerting(WishItem w)
        {
            double t;
            return !w.NoPrice && !w.Free && w.Final > 0 && (w.Discount > 0 || (S.SaleTargets.TryGetValue(w.AppId, out t) && t > 0 && w.Final <= t * 100));
        }

        // once a day while sale alerts are on (and on demand)
        void CheckSales(bool manual)
        {
            if (Snap.On || wishChecking) return;
            if (!S.SaleAlerts) { if (manual) Status("Turn on sale alerts in Settings first."); return; }
            DateTime last;
            if (!manual && DateTime.TryParse(S.LastSaleCheck, out last) && (DateTime.Now - last).TotalHours < 23) return;
            var profile = S.SteamProfile; var old = Wishlist.Load();
            wishChecking = true; if (manual) Status("Checking your Steam wishlist...");
            new Thread(() =>
            {
                string err; var id = Wishlist.SteamId(profile, out err);
                WishCache c = null; if (id != null) c = Wishlist.Check(id, old, out err);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    wishChecking = false;
                    if (c == null) { if (manual) Status(err); return; }
                    S.LastSaleCheck = DateTime.Now.ToString("o"); Wishlist.Save(c);
                    var hits = c.Items.Where(Alerting).ToList();
                    var fresh = hits.Where(w => !S.SaleTold.Contains(w.AppId + ":" + w.Final)).ToList();
                    S.SaleTold = hits.Select(w => w.AppId + ":" + w.Final).ToList(); S.Save();
                    if (fresh.Count > 0 && tray != null)
                    {
                        var text = string.Join(", ", fresh.OrderByDescending(w => w.Discount).Take(3).Select(w => WishName(w) + (w.Discount > 0 ? " -" + w.Discount + "%" : " " + w.Price)));
                        if (fresh.Count > 3) text += " and " + (fresh.Count - 3) + " more";
                        tray.ShowBalloonTip(8000, fresh.Count == 1 ? "On sale from your wishlist" : fresh.Count + " wishlist games on sale", text, WF.ToolTipIcon.None);
                    }
                    if (manual) Status(c.Items.Count == 0 ? "Your wishlist is empty or private - Steam only shares public wishlists." : c.Items.Count + " games on your wishlist, " + hits.Count + " on sale or below your price.");
                    if (wishPanel.Visibility == Visibility.Visible) FillWishlist();
                    UpdateWishButton();
                }));
            }) { IsBackground = true }.Start();
        }
        void UpdateWishButton() { if (wishBtn != null) wishBtn.Visibility = S.SaleAlerts ? Visibility.Visible : Visibility.Collapsed; }

        Border BuildWishlist()
        {
            var b = new Border { Background = B("#211B2A"), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24, 16, 12, 18), Width = 760, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 62, 0, 24), Visibility = Visibility.Collapsed };
            b.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, Opacity = 0.6, ShadowDepth = 4 };
            var dp = new DockPanel();
            var sh = new DockPanel { Margin = new Thickness(0, 0, 12, 12) };
            var close = new Button { Content = "x", Tag = "icon", Style = (Style)FindResource("Btn"), Width = 30, Height = 28, Padding = new Thickness(0), ToolTip = "Close" };
            close.Click += (s, e) => wishPanel.Visibility = Visibility.Collapsed;
            DockPanel.SetDock(close, Dock.Right); sh.Children.Add(close);
            var now = new Button { Content = "Check now", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0) };
            now.Click += (s, e) => CheckSales(true);
            DockPanel.SetDock(now, Dock.Right); sh.Children.Add(now);
            var ic = Icons.Make("cart-shopping", 16, B("#F07820")); ic.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(ic, Dock.Left); sh.Children.Add(ic);
            sh.Children.Add(new TextBlock { Text = "Your Steam wishlist", FontWeight = FontWeights.Bold, FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(sh, Dock.Top); dp.Children.Add(sh);
            wishBody = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            dp.Children.Add(new ScrollViewer { Content = wishBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 600 });
            b.Child = dp; return b;
        }
        void OpenWishlist()
        {
            statsBtn.IsChecked = false; storageBtn.IsChecked = false; gearBtn.IsChecked = false; newsPanel.Visibility = Visibility.Collapsed;
            wishPanel.Visibility = Visibility.Visible; FillWishlist();
        }
        void FillWishlist()
        {
            wishBody.Children.Clear();
            var c = Wishlist.Load();
            DateTime when; var checkedText = DateTime.TryParse(c.Checked, null, System.Globalization.DateTimeStyles.RoundtripKind, out when) ? "Checked " + HubSettings.Ago(when.ToUniversalTime()).ToLowerInvariant() + " - Hub looks once a day." : "Not checked yet - press Check now.";
            wishBody.Children.Add(new TextBlock { Text = checkedText + " Set a price on any game to be told when it drops below it.", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            var sale = c.Items.Where(Alerting).OrderByDescending(w => w.Discount).ToList();
            var rest = c.Items.Where(w => !Alerting(w)).OrderBy(w => WishName(w), StringComparer.CurrentCultureIgnoreCase).ToList();
            wishBody.Children.Add(SectionLabel("ON SALE" + (sale.Count > 0 ? " (" + sale.Count + ")" : ""), "fire"));
            if (sale.Count == 0) wishBody.Children.Add(new TextBlock { Text = c.Items.Count == 0 ? "Nothing yet." : "Nothing on your wishlist is on sale right now.", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 12) });
            foreach (var w in sale) wishBody.Children.Add(WishRow(w, true));
            wishBody.Children.Add(SectionLabel("WISHLIST" + (rest.Count > 0 ? " (" + rest.Count + ")" : ""), "star"));
            if (c.Items.Count == 0) wishBody.Children.Add(new TextBlock { Text = "Your wishlist shows up here once Hub has checked it. Steam only shares wishlists of public profiles.", Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap });
            foreach (var w in rest.Take(200)) wishBody.Children.Add(WishRow(w, false));
        }
        FrameworkElement WishRow(WishItem w, bool hot)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var store = new Button { Content = "Store page", Tag = "icon", Width = 30, Height = 28, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Open it in Steam" };
            var appid = w.AppId; store.Click += (s, e) => OpenUrl("steam://store/" + appid);
            DockPanel.SetDock(store, Dock.Right); row.Children.Add(store);
            // your price
            double t; S.SaleTargets.TryGetValue(w.AppId, out t);
            var box = new TextBox { Style = (Style)FindResource("Field"), Width = 70, Text = t > 0 ? t.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "", ToolTip = "Tell me when it's below this price (" + (w.Currency.Length > 0 ? w.Currency : "your currency") + ") - empty = only sales", VerticalAlignment = VerticalAlignment.Center };
            box.LostFocus += (s, e) =>
            {
                double v; var txt = box.Text.Trim().Replace(',', '.');
                if (txt.Length == 0) S.SaleTargets.Remove(appid);
                else if (double.TryParse(txt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && v > 0) S.SaleTargets[appid] = v;
                else { box.Text = ""; S.SaleTargets.Remove(appid); }
                S.Save();
            };
            DockPanel.SetDock(box, Dock.Right); row.Children.Add(box);
            var below = new TextBlock { Text = "alert below", Foreground = (Brush)FindResource("Muted"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) };
            DockPanel.SetDock(below, Dock.Right); row.Children.Add(below);
            var price = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Width = 170 };
            if (w.Discount > 0) price.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = B("#6ED28C"), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(0, 0, 8, 0), Child = new TextBlock { Text = "-" + w.Discount + "%", FontWeight = FontWeights.Bold, FontSize = 11.5, Foreground = B("#0E2416") } });
            if (w.Discount > 0 && w.Initial > 0) price.Children.Add(new TextBlock { Text = Money(w.Initial, ""), Foreground = (Brush)FindResource("Muted"), TextDecorations = TextDecorations.Strikethrough, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            price.Children.Add(new TextBlock { Text = w.Free ? "Free" : w.NoPrice || w.Final <= 0 ? "No price" : (w.Price.Length > 0 ? w.Price : Money(w.Final, w.Currency)), FontWeight = FontWeights.SemiBold, Foreground = hot ? B("#6ED28C") : (Brush)FindResource("Text"), VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(price, Dock.Right); row.Children.Add(price);
            row.Children.Add(new TextBlock { Text = WishName(w), FontSize = 13.5, FontWeight = hot ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            return row;
        }

        // Settings: the sale alerts block
        void AddSaleSettings(StackPanel sp)
        {
            var on = new CheckBox { Content = new TextBlock { Text = "Sale alerts for my Steam wishlist (Hub checks once a day)", TextWrapping = TextWrapping.Wrap }, IsChecked = S.SaleAlerts, Style = (Style)FindResource("Switch") };
            var row = new DockPanel { Margin = new Thickness(52, 0, 0, 4) };
            var show = new Button { Content = "Wishlist", Style = (Style)FindResource("Btn"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
            show.Click += (s, e) => OpenWishlist();
            DockPanel.SetDock(show, Dock.Right); row.Children.Add(show);
            var prof = new TextBox { Style = (Style)FindResource("Field"), Text = S.SteamProfile ?? "", ToolTip = "Your SteamID64 or your Steam profile link (steamcommunity.com/id/... or /profiles/...). Your wishlist must be public." };
            prof.LostFocus += (s, e) => { var v = prof.Text.Trim(); if (v != S.SteamProfile) { S.SteamProfile = v; S.LastSaleCheck = ""; S.Save(); if (S.SaleAlerts && v.Length > 0) CheckSales(true); } };
            row.Children.Add(prof);
            row.Visibility = S.SaleAlerts ? Visibility.Visible : Visibility.Collapsed;
            var note = new TextBlock { Text = "Your SteamID64 or profile link - your wishlist has to be public. Only Steam's public pages are used; nothing is signed in.", Foreground = (Brush)FindResource("Muted"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(52, 0, 0, 6), Visibility = row.Visibility };
            on.Click += (s, e) =>
            {
                S.SaleAlerts = on.IsChecked == true; S.Save(); UpdateWishButton();
                row.Visibility = note.Visibility = S.SaleAlerts ? Visibility.Visible : Visibility.Collapsed;
                if (S.SaleAlerts && (S.SteamProfile ?? "").Length > 0) CheckSales(true);
            };
            sp.Children.Add(on); sp.Children.Add(row); sp.Children.Add(note);
        }
    }
}
