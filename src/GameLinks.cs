// Hallow Hub v2.12 - links per game (like Playnite's Links): the Steam store, community hub and guides, PCGamingWiki and
// HowLongToBeat (both open in your browser - Hub fetches nothing from them), plus links of your own (wiki, Discord, tracker...).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace HallowHub
{
    public static class GameLinks
    {
        // name, url - worked out from the game itself, nothing is downloaded
        public static List<string[]> Auto(Game g)
        {
            var l = new List<string[]>();
            var name = Uri.EscapeDataString(((g.OriginalName ?? g.Name) ?? "").Trim());
            var steam = new[] { g }.Concat(g.Alts).FirstOrDefault(c => c.Source == "steam" && c.Id.StartsWith("steam:"));
            if (steam != null)
            {
                var id = steam.Id.Substring(6);
                l.Add(new[] { "Steam store page", "https://store.steampowered.com/app/" + id + "/" });
                l.Add(new[] { "Steam community hub", "https://steamcommunity.com/app/" + id });
                l.Add(new[] { "Steam guides", "https://steamcommunity.com/app/" + id + "/guides/" });
                l.Add(new[] { "PCGamingWiki (fixes, settings, saves)", "https://www.pcgamingwiki.com/api/appid.php?appid=" + id });
            }
            else if (g.Source != "rom") l.Add(new[] { "PCGamingWiki (fixes, settings, saves)", "https://www.pcgamingwiki.com/w/index.php?search=" + name });
            if (name.Length > 0) l.Add(new[] { "HowLongToBeat (how long it takes)", "https://howlongtobeat.com/?q=" + name });
            return l;
        }

        // "name|url" -> { name, url }, or null when it isn't a web link
        public static string[] Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var i = s.IndexOf('|'); if (i < 0) return null;
            var u = Clean(s.Substring(i + 1)); if (u == null) return null;
            return new[] { NameFor(s.Substring(0, i), u), u };
        }
        // only http / https links; "example.com/page" becomes https://example.com/page
        public static string Clean(string url)
        {
            url = (url ?? "").Trim(); if (url.Length == 0 || url.Any(char.IsWhiteSpace)) return null;
            if (!url.Contains("://")) url = "https://" + url;
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) || string.IsNullOrEmpty(u.Host) || !u.Host.Contains(".")) return null;
            return u.AbsoluteUri;
        }
        public static string NameFor(string name, string url)
        {
            name = (name ?? "").Trim().Replace("|", "/");
            if (name.Length == 0) { try { name = new Uri(url).Host; if (name.StartsWith("www.")) name = name.Substring(4); } catch { name = "Link"; } }
            return name.Length > 40 ? name.Substring(0, 40) : name;
        }
        public static string Make(string name, string url) { var u = Clean(url); return u == null ? null : NameFor(name, u) + "|" + u; }
    }

    public partial class HubWindow
    {
        Popup linksPop; StackPanel linksList; TextBox linkName, linkUrl; TextBlock linkErr; Game linksFor; Button linksBtn;

        void ShowLinks(UIElement anchor)
        {
            if (sel == null) return;
            if (linksPop == null)
            {
                var sp = new StackPanel();
                var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                var hi = Icons.Make("bookmark", 13, B("#F07820")); hi.Margin = new Thickness(0, 0, 8, 0); head.Children.Add(hi);
                head.Children.Add(new TextBlock { Text = "Links", FontWeight = FontWeights.Bold, Foreground = B("#ECE8F4") });
                sp.Children.Add(head);
                linksList = new StackPanel(); sp.Children.Add(linksList);
                sp.Children.Add(new TextBlock { Text = "Add your own", Foreground = B("#8F88A0"), FontSize = 11.5, Margin = new Thickness(0, 10, 0, 4) });
                linkName = new TextBox { Style = (Style)FindResource("Field"), ToolTip = "Name (optional) - like Wiki, Discord or Build planner" };
                linkUrl = new TextBox { Style = (Style)FindResource("Field"), Margin = new Thickness(0, 6, 0, 0), ToolTip = "The web address (https://...)" };
                var nameRow = new Grid(); nameRow.Children.Add(linkName); nameRow.Children.Add(Hint(linkName, "Name (optional)"));
                var urlRow = new Grid(); urlRow.Children.Add(linkUrl); urlRow.Children.Add(Hint(linkUrl, "https://..."));
                sp.Children.Add(nameRow); sp.Children.Add(urlRow);
                var add = new Button { Content = "Add link", Style = (Style)FindResource("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
                add.Click += (s, e) => AddLink();
                linkUrl.KeyDown += (s, e) => { if (e.Key == Key.Enter) { AddLink(); e.Handled = true; } };
                linkErr = new TextBlock { Foreground = B("#F0BE5A"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
                sp.Children.Add(add); sp.Children.Add(linkErr);
                sp.Children.Add(new TextBlock { Text = "Links open in your browser. PCGamingWiki and HowLongToBeat are their own sites - Hub doesn't fetch anything from them.", Foreground = B("#8F88A0"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
                linksPop = new Popup { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom, VerticalOffset = 8,
                    Child = new Border { Width = 360, Background = B("#241E2E"), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 12), Child = sp } };
                linksPop.Closed += (s, e) => { S.Save(); ShowHero(); };
            }
            linksFor = sel; linkName.Text = ""; linkUrl.Text = ""; linkErr.Visibility = Visibility.Collapsed; FillLinks();
            linksPop.PlacementTarget = anchor; linksPop.IsOpen = true;
        }
        TextBlock Hint(TextBox box, string text)
        {
            var t = new TextBlock { Text = text, Foreground = B("#6F6880"), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 0), FontSize = 12 };
            box.TextChanged += (s, e) => t.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            return t;
        }
        void FillLinks()
        {
            linksList.Children.Clear(); if (linksFor == null) return;
            var d = S.For(linksFor.Id);
            Action<string, string, string, Action> row = (name, url, icon, remove) =>
            {
                var r = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                if (remove != null)
                {
                    var rm = new Button { Content = "x", Tag = "icon", Width = 26, Height = 26, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Remove this link" };
                    rm.Click += (s, e) => remove();
                    DockPanel.SetDock(rm, Dock.Right); r.Children.Add(rm);
                }
                var u = url;
                var b = new Button { Style = (Style)FindResource("Btn"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 4, 10, 5), ToolTip = url,
                    Content = Icons.WithText(icon, name, 11, B(remove != null ? "#F07820" : "#C9B2F5")) };
                b.Click += (s, e) => { OpenUrl(u); linksPop.IsOpen = false; };
                r.Children.Add(b);
                linksList.Children.Add(r);
            };
            foreach (var a in GameLinks.Auto(linksFor)) row(a[0], a[1], "arrow-up-right-from-square", null);
            foreach (var raw in d.Links.ToList())
            {
                var p = GameLinks.Parse(raw); if (p == null) continue;
                var keep = raw; row(p[0], p[1], "bookmark", () => { d.Links.Remove(keep); S.Save(); FillLinks(); Status("Link removed."); });
            }
        }
        void AddLink()
        {
            if (linksFor == null) return;
            var made = GameLinks.Make(linkName.Text, linkUrl.Text);
            if (made == null) { linkErr.Text = "That doesn't look like a web address - try something like https://example.com/page."; linkErr.Visibility = Visibility.Visible; return; }
            var d = S.For(linksFor.Id);
            if (!d.Links.Any(x => string.Equals(x, made, StringComparison.OrdinalIgnoreCase))) d.Links.Add(made);
            while (d.Links.Count > 30) d.Links.RemoveAt(0);
            S.Save(); linkName.Text = ""; linkUrl.Text = ""; linkErr.Visibility = Visibility.Collapsed; FillLinks();
            Status("Link added to " + linksFor.Name + ".");
        }
        // the same links as a submenu on a game's right-click menu
        MenuItem LinksMenu(Game g)
        {
            var root = new MenuItem { Header = "Links", Icon = Icons.Make("bookmark", 13, B("#ECE8F4")) };
            foreach (var a in GameLinks.Auto(g)) { var u = a[1]; var mi = new MenuItem { Header = a[0], ToolTip = u }; mi.Click += (s, e) => OpenUrl(u); root.Items.Add(mi); }
            var own = S.For(g.Id).Links.Select(GameLinks.Parse).Where(p => p != null).ToList();
            if (own.Count > 0) root.Items.Add(new Separator());
            foreach (var p in own) { var u = p[1]; var mi = new MenuItem { Header = p[0], ToolTip = u }; mi.Click += (s, e) => OpenUrl(u); root.Items.Add(mi); }
            root.Items.Add(new Separator());
            var edit = new MenuItem { Header = "Add a link..." }; edit.Click += (s, e) => { Select(g); Dispatcher.BeginInvoke(new Action(() => ShowLinks(linksBtn))); }; root.Items.Add(edit);
            return root;
        }
    }
}
