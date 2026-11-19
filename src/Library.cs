// Hallow Hub - finds installed games: Steam, Battle.net, Epic Games, plus games you add yourself.
// Only reads launcher files and the registry; artwork comes from Steam's own on-disk cache (nothing is downloaded).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace HallowHub
{
    public static class Library
    {
        // Steam tools that aren't games
        static readonly Regex NotAGame = new Regex(@"Redistributable|Steamworks|Proton|SteamVR|Steam Linux Runtime|Wallpaper Engine|Soundtrack|Dedicated Server|\bSDK\b", RegexOptions.IgnoreCase);
        static readonly HashSet<string> NotAGameIds = new HashSet<string> { "228980", "431960", "1070560", "1391110", "1628350", "250820" };

        public static List<Game> Scan(List<Game> custom)
        {
            var list = new List<Game>();
            try { list.AddRange(Steam()); } catch { }
            try { list.AddRange(BattleNet()); } catch { }
            try { list.AddRange(Epic()); } catch { }
            foreach (var c in custom) if (c != null && !string.IsNullOrEmpty(c.Launch)) { c.IconExe = c.Launch; list.Add(c); }
            return list.GroupBy(g => g.Id).Select(x => x.First()).ToList();
        }

        // ---- Steam: libraryfolders.vdf -> steamapps\appmanifest_*.acf
        public static string SteamPath()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                var p = k == null ? null : k.GetValue("SteamPath") as string;
                return string.IsNullOrEmpty(p) ? null : p.Replace('/', '\\');
            }
        }
        static IEnumerable<Game> Steam()
        {
            var steam = SteamPath(); if (steam == null || !Directory.Exists(steam)) yield break;
            var libs = new List<string> { steam };
            var vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf, Encoding.UTF8), "\"path\"\\s+\"([^\"]+)\""))
                    libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            var seen = new HashSet<string>();
            foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var apps = Path.Combine(lib, "steamapps"); if (!Directory.Exists(apps)) continue;
                foreach (var acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
                {
                    string t; try { t = File.ReadAllText(acf, Encoding.UTF8); } catch { continue; }
                    var id = Field(t, "appid"); var name = Field(t, "name"); var dir = Field(t, "installdir");
                    if (id == null || name == null || dir == null || !seen.Add(id)) continue;
                    if (NotAGameIds.Contains(id) || NotAGame.IsMatch(name)) continue;
                    var g = new Game { Id = "steam:" + id, Name = Clean(name), Source = "steam", InstallDir = Path.Combine(apps, "common", dir), Launch = "steam://rungameid/" + id };
                    var cache = Path.Combine(steam, @"appcache\librarycache", id);
                    if (Directory.Exists(cache))
                    {
                        var files = Directory.GetFiles(cache, "*", SearchOption.AllDirectories);
                        Func<string, string> pick = n => files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), n, StringComparison.OrdinalIgnoreCase));
                        g.Cover = pick("library_600x900.jpg") ?? pick("library_capsule.jpg");
                        g.Hero = pick("library_hero.jpg"); g.Logo = pick("logo.png"); g.Header = pick("library_header.jpg") ?? pick("header.jpg");
                    }
                    yield return g;
                }
            }
        }
        static string Field(string acf, string key) { var m = Regex.Match(acf, "\"" + key + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase); return m.Success ? m.Groups[1].Value : null; }
        static string Clean(string n) { return n.Replace("\u00AE", "").Replace("\u2122", "").Trim(); }   // drop (R) and TM

        // ---- Battle.net: Blizzard games in the uninstall list, launched through Battle.net
        static readonly Dictionary<string, string> BnetCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "Overwatch", "Pro" }, { "Diablo IV", "Fen" }, { "Diablo III", "D3" }, { "Diablo II: Resurrected", "OSI" }, { "World of Warcraft", "WoW" },
            { "Hearthstone", "WTCG" }, { "StarCraft II", "S2" }, { "StarCraft", "S1" }, { "Heroes of the Storm", "Hero" }, { "Warcraft III: Reforged", "W3" }, { "Diablo Immortal", "ANBS" } };
        static IEnumerable<Game> BattleNet()
        {
            string bnet = null; var games = new List<Game>();
            foreach (var e in Uninstall())
            {
                if (!string.Equals(e.Publisher, "Blizzard Entertainment", StringComparison.OrdinalIgnoreCase)) continue;
                if (e.Name == "Battle.net") { var exe = Path.Combine(e.Location ?? "", "Battle.net.exe"); if (File.Exists(exe)) bnet = exe; continue; }
                if (string.IsNullOrEmpty(e.Location) || !Directory.Exists(e.Location)) continue;
                var icon = e.Icon != null && File.Exists(e.Icon.Split(',')[0].Trim('"')) ? e.Icon.Split(',')[0].Trim('"') : null;
                games.Add(new Game { Id = "bnet:" + e.Name, Name = Clean(e.Name), Source = "battlenet", InstallDir = e.Location.TrimEnd('\\'), Launch = icon ?? "", IconExe = icon });
            }
            foreach (var g in games)
            {
                string code;
                if (bnet != null && BnetCodes.TryGetValue(g.Name, out code)) { g.Launch = bnet; g.Args = "--exec=\"launch " + code + "\""; }
                if (!string.IsNullOrEmpty(g.Launch)) yield return g;
            }
        }
        class Entry { public string Name, Publisher, Location, Icon; }
        static IEnumerable<Entry> Uninstall()
        {
            var roots = new[] { Tuple.Create(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall") };
            foreach (var r in roots)
                using (var k = r.Item1.OpenSubKey(r.Item2))
                {
                    if (k == null) continue;
                    foreach (var n in k.GetSubKeyNames())
                        using (var s = k.OpenSubKey(n))
                        {
                            if (s == null) continue;
                            var name = s.GetValue("DisplayName") as string; if (string.IsNullOrEmpty(name)) continue;
                            yield return new Entry { Name = name, Publisher = s.GetValue("Publisher") as string, Location = s.GetValue("InstallLocation") as string, Icon = s.GetValue("DisplayIcon") as string };
                        }
                }
        }

        // ---- Epic Games: one JSON manifest per installed game
        static IEnumerable<Game> Epic()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
            if (!Directory.Exists(dir)) yield break;
            var js = new JavaScriptSerializer();
            foreach (var f in Directory.GetFiles(dir, "*.item"))
            {
                Dictionary<string, object> m; try { m = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(f)); } catch { continue; }
                Func<string, string> s = k => { object v; return m.TryGetValue(k, out v) ? v as string : null; };
                var name = s("DisplayName"); var app = s("AppName"); var loc = s("InstallLocation"); var exe = s("LaunchExecutable");
                if (name == null || app == null || loc == null || !Directory.Exists(loc)) continue;
                object cats; if (m.TryGetValue("AppCategories", out cats) && cats is System.Collections.ArrayList && !((System.Collections.ArrayList)cats).Contains("games")) continue;
                var full = exe == null ? null : Path.Combine(loc, exe);
                yield return new Game { Id = "epic:" + app, Name = Clean(name), Source = "epic", InstallDir = loc.TrimEnd('\\'),
                    Launch = "com.epicgames.launcher://apps/" + Uri.EscapeDataString(app) + "?action=launch&silent=true", IconExe = full != null && File.Exists(full) ? full : null };
            }
        }

        // ---- a game you add yourself (any .exe)
        public static Game FromExe(string exe)
        {
            var name = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).ProductName;
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(exe);
            return new Game { Id = "custom:" + exe.ToLowerInvariant(), Name = Clean(name), Source = "custom", InstallDir = Path.GetDirectoryName(exe), Launch = exe, IconExe = exe };
        }
    }
}
