// Hallow Hub - saved data: per-game playtime and choices, and the app's settings (%APPDATA%\HallowHub\hub.json).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace HallowHub
{
    // A game found on this PC (not saved - found again on every scan, except games you add yourself).
    [DataContract]
    public class Game
    {
        [DataMember] public string Id;          // "steam:2507950", "bnet:Overwatch", "epic:Fortnite", "custom:<path>"
        [DataMember] public string Name;
        [DataMember] public string Source;      // steam, battlenet, epic, custom
        [DataMember] public string InstallDir;  // processes running from here count as this game
        [DataMember] public string Launch;      // URI or exe path
        [DataMember] public string Args = "";
        public string Cover, Hero, Logo, Header, IconExe; public string OriginalName;
        public string Platform, RomPath;   // v2.9: ROMs - the emulator profile's name and the ROM file
        // v1.1
        public DateTime UpdatedAt;              // when the install last changed (picks the main copy of a duplicate)
        public long SizeBytes; public string Drive; public string Uninstall;   // v1.4: the launcher's own uninstall command (Battle.net)
        public bool NeedsUpdate, Updating; public double Progress = -1;   // Steam download, 0..1
        public double SteamMinutes, Steam2Weeks; public DateTime? LauncherLast;   // the launcher's own last-played (Steam; Battle.net since 2.1)
        public List<Game> Alts = new List<Game>();   // the same game from another store (merged into one card)
        public Game Parent;                          // set on an alt: the card it belongs to
        public string SourceName { get { return Source == "steam" ? "Steam" : Source == "battlenet" ? "Battle.net" : Source == "epic" ? "Epic Games" : Source == "ea" ? "EA app" : Source == "ubisoft" ? "Ubisoft Connect" : Source == "gog" ? "GOG" : Source == "riot" ? "Riot" : Source == "xbox" ? "Xbox" : Source == "rom" ? "Emulators" : "Added by you"; } }
    }

    [DataContract]
    public class GameData
    {
        [DataMember] public double Minutes;      // total time played
        [DataMember] public string LastPlayed = "";
        [DataMember] public int Sessions;
        [DataMember] public bool Hidden, Favorite;
        [DataMember] public string Preset = "";  // Hallow Mixer preset to switch to when the game starts ("" = leave it)
        [DataMember] public bool GameSession;    // Hallow Toolkit game session while it runs
        [DataMember] public List<string> Days = new List<string>();   // "yyyy-MM-dd=minutes" for the last 14 days
        [DataMember] public string PlayOn = "";  // v1.1: which copy to start when the game is in two stores (a Game.Id)
        [DataMember] public string LaunchArgs = "";                  // v1.2: extra launch options
        [DataMember] public bool Admin;                              // v1.2: run as administrator (games started from an .exe)
        [DataMember] public List<string> Tags = new List<string>();   // v1.2: collections it's in
        [DataMember] public string CustomName = "", CustomCover = "", CustomHero = "";   // v1.5: your own name and pictures
        [DataMember] public List<string> OpenWith = new List<string>();   // v1.6: apps to open with the game
        [DataMember] public bool KeepOpen;                                 // v1.6: leave them open when the game closes
        [DataMember] public int DailyLimit;                                // v1.6: minutes a day (0 = no limit)
        [DataMember] public List<string> SessionLog = new List<string>(); // v1.6: "start (o)|minutes", newest last, up to 40
        [DataMember] public string LimitTold = "";                         // v1.6: the day the limit reminder was shown
        [DataMember] public bool Pinned;                                   // v1.7: always at the top of the library
        [DataMember] public string Notes = "";                             // v1.7: your notes (keybinds, loadouts, codes)
        [DataMember] public string LastCopy = "", LastCopyAt = "";
        [DataMember] public string Status = "";                            // v2.10: plan, playing, beaten, completed, hold, abandoned
        [DataMember] public int Rating;                                    // v2.10: your stars, 0-5         // v2.1: which copy of a merged game Hub last saw running, and when
        [DataMember] public List<string> Links = new List<string>();      // v2.12: your own links, "name|https://..."
        [DataMember] public List<string> CloseFirst = new List<string>(); // v2.12: apps to close when the game starts (exe paths)
        [DataMember] public bool NoReopen;                                 // v2.12: don't open them again when the game closes
        [DataMember] public string PowerPlan = "";                         // v2.14: power plan while it runs (a GUID; "" = the all-games choice, "none" = leave it)
        [OnDeserializing] void D(StreamingContext c) { LastPlayed = ""; Preset = ""; Days = new List<string>(); PlayOn = ""; LaunchArgs = ""; Tags = new List<string>(); CustomName = ""; CustomCover = ""; CustomHero = ""; OpenWith = new List<string>(); SessionLog = new List<string>(); LimitTold = ""; Notes = ""; LastCopy = ""; LastCopyAt = ""; Status = ""; Links = new List<string>(); CloseFirst = new List<string>(); PowerPlan = ""; }

        public DateTime? Last { get { DateTime d; return DateTime.TryParse(LastPlayed, null, System.Globalization.DateTimeStyles.RoundtripKind, out d) ? d : (DateTime?)null; } }
        public void AddMinutes(double m, DateTime when)
        {
            Minutes += m; LastPlayed = when.ToString("o");
            string key = when.ToString("yyyy-MM-dd"); bool found = false;
            for (int i = 0; i < Days.Count; i++)
            {
                var p = Days[i].Split('='); double v;
                if (p.Length == 2 && p[0] == key && double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                { Days[i] = key + "=" + (v + m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); found = true; }
            }
            if (!found) Days.Add(key + "=" + m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            var cutoff = when.Date.AddDays(-400).ToString("yyyy-MM-dd");
            Days.RemoveAll(d => string.CompareOrdinal(d.Split('=')[0], cutoff) < 0);
        }
        public double MinutesOn(DateTime day)
        {
            var k = day.ToString("yyyy-MM-dd");
            foreach (var d in Days) { var p = d.Split('='); double v; if (p.Length == 2 && p[0] == k && double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return v; }
            return 0;
        }
        public double MinutesSince(DateTime from)
        {
            double t = 0; var k = from.ToString("yyyy-MM-dd");
            foreach (var d in Days) { var p = d.Split('='); double v; if (p.Length == 2 && string.CompareOrdinal(p[0], k) >= 0 && double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) t += v; }
            return t;
        }
    }

    [DataContract]
    public class HubSettings
    {
        [DataMember] public Dictionary<string, GameData> Games = new Dictionary<string, GameData>();
        [DataMember] public List<Game> Custom = new List<Game>();
        [DataMember] public string Sort = "recent";       // recent, played, name
        [DataMember] public string Filter = "all";        // all, steam, battlenet, epic, custom, fav
        [DataMember] public string Selected = "";
        [DataMember] public bool TrayOnClose = true;      // keep running (and counting playtime) when the window is closed
        [DataMember] public bool ShowHidden;
        [DataMember] public double WinW = -1, WinH = -1;
        [DataMember] public string LastSeenVersion = "";
        [DataMember] public string LastUpdateCheck = "";
        [DataMember] public int BreakMinutes;   // v1.6: remind me to take a break after this long in one go (0 = off)
        [DataMember] public string View = "grid";      // v1.7: grid or list
        [DataMember] public double CardScale = 1;      // v1.7: cover size (0.8 small, 1 medium, 1.25 large)
        [DataMember] public bool StillBanner;          // v1.7: turn off the slow banner movement
        [DataMember] public bool SideClosed, StartOpen;   // v2.28: the game list on the left closed; "When this game starts" opened
        [DataMember] public bool DiscordOnStart, DiscordRecap;   // v1.8: post to Discord (through Hallow Toolkit's link) - both off unless you turn them on
        [DataMember] public bool WeeklyRecap = true;              // v1.8: a recap of last week on Mondays
        [DataMember] public string LastRecap = "";
        [DataMember] public List<string> Collections = new List<string>();   // v1.2: your own groups, in your order
        // v2.3: filters that combine with the chips and the sort ("" = any)
        [DataMember] public List<string> FStores = new List<string>();      // only these stores (empty = all)
        [DataMember] public string FSize = "", FLast = "", FTime = "";     // size: small/mid/big; last played: week/month/old/never; playtime: new/some/lots
        [DataMember] public bool UseHallowAccent = true;                 // v2.5: use the accent picked in Hallow Toolkit
        [DataMember] public string LastCrashShown = "";                    // v2.5: the crash report already told about
        // v2.7: sale alerts for your Steam wishlist (off unless you turn them on)
        [DataMember] public bool SaleAlerts;
        [DataMember] public string SteamProfile = "";                    // SteamID64 or profile link
        [DataMember] public Dictionary<string, double> SaleTargets = new Dictionary<string, double>();   // appid -> tell me below this price
        [DataMember] public string LastSaleCheck = "";
        [DataMember] public List<string> SaleTold = new List<string>();   // "appid:final price" already told
        [DataMember] public List<Emulator> Emulators = new List<Emulator>();   // v2.9: your emulator profiles
        [DataMember] public string FStatus = "";                          // v2.10: only games with this play status ("none" = no status)
        [DataMember] public List<string> FTags = new List<string>();     // v2.8: genres / features a game must have
        [DataMember] public bool OneIcon = true;                         // v2.4: One Hallow icon - the other Hallow apps use Hub's tray icon
        [DataMember] public string PowerPlanAll = "";                    // v2.14: power plan while any game runs (a GUID, "" = leave it)
        [DataMember] public string PowerRestore = "";                    // v2.14: "game|plan before|plan set" while Hub has switched the plan
        [DataMember] public string Look = "hallow";                      // v2.15: Look - hallow (follow the Hallow family light / dark mode), light or dark
        [DataMember] public string PlacesUrl = "";                       // v2.17: your Hallow Places server (only its /healthz is read; "" = not set up)
        [DataMember] public bool RestartAfterUpdates = true;             // v2.19: start a family app again (in the tray) after it was updated while running
        [DataMember] public List<string> RestartOptOut = new List<string>();   // v2.19: ...except these apps
        [DataMember] public bool CheckupWeekly = true;                   // v2.21: a quiet Family checkup once a week (never during a game)
        [DataMember] public string CheckupLast = "";                     // v2.21: when the last checkup ran (ISO)
        [DataMember] public int CheckupProblems, CheckupWarnings;        // v2.21: what it found
        [OnDeserializing] void D(StreamingContext c)
        {
            PlacesUrl = ""; RestartAfterUpdates = true; RestartOptOut = new List<string>(); CheckupWeekly = true; CheckupLast = "";
            Games = new Dictionary<string, GameData>(); Custom = new List<Game>(); Sort = "recent"; Filter = "all"; Selected = ""; TrayOnClose = true; WinW = -1; WinH = -1; LastSeenVersion = ""; LastUpdateCheck = ""; Collections = new List<string>(); View = "grid"; CardScale = 1; WeeklyRecap = true; LastRecap = "";
            FStores = new List<string>(); FSize = ""; FLast = ""; FTime = ""; OneIcon = true; UseHallowAccent = true; Look = "hallow"; LastCrashShown = ""; SteamProfile = ""; SaleTargets = new Dictionary<string, double>(); LastSaleCheck = ""; SaleTold = new List<string>(); FTags = new List<string>(); Emulators = new List<Emulator>(); FStatus = ""; PowerPlanAll = ""; PowerRestore = "";
        }

        public static string Dir { get { return TestLibrary.On ? Path.Combine(TestLibrary.Root, "HallowHub") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HallowHub"); } }   // v2.28: tests use their own folder
        static string FilePath { get { return Path.Combine(Dir, "hub.json"); } }
        public static bool Frozen;   // snapshots: never write

        public GameData For(string id)
        {
            GameData g; if (!Games.TryGetValue(id, out g)) { g = new GameData(); Games[id] = g; } return g;
        }

        public static HubSettings Load()
        {
            if (TestLibrary.On && !File.Exists(FilePath)) return TestLibrary.Settings();   // v2.28: made-up playtime for tests
            try
            {
                if (File.Exists(FilePath))
                    using (var f = File.OpenRead(FilePath)) { var s = (HubSettings)new DataContractJsonSerializer(typeof(HubSettings), Opts()).ReadObject(f); if (s != null) return s; }
            }
            catch { try { File.Copy(FilePath, FilePath + ".broken", true); } catch { } }
            return new HubSettings();
        }
        public void Save()
        {
            if (Frozen) return;
            try
            {
                Directory.CreateDirectory(Dir);
                var tmp = FilePath + ".tmp";
                using (var f = File.Create(tmp)) new DataContractJsonSerializer(typeof(HubSettings), Opts()).WriteObject(f, this);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
            }
            catch { }
        }
        static DataContractJsonSerializerSettings Opts() { return new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }; }

        public static string Duration(double minutes)
        {
            if (minutes < 1) return "Not played yet";
            int m = (int)Math.Round(minutes);
            if (m < 60) return m + " min";
            return (m / 60) + "h " + (m % 60).ToString("00") + "m";
        }
        public static string Ago(DateTime? when)
        {
            if (when == null) return "Never";
            var d = DateTime.Now - when.Value.ToLocalTime();
            if (d.TotalMinutes < 2) return "Just now";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + " min ago";
            if (d.TotalHours < 24) return (int)d.TotalHours + (d.TotalHours < 2 ? " hour ago" : " hours ago");
            if (d.TotalDays < 2) return "Yesterday";
            if (d.TotalDays < 30) return (int)d.TotalDays + " days ago";
            return when.Value.ToLocalTime().ToString("d MMM yyyy");
        }
    }
}
