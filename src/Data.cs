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
        public string Cover, Hero, Logo, Header, IconExe;
        // v1.1
        public DateTime UpdatedAt;              // when the install last changed (picks the main copy of a duplicate)
        public long SizeBytes; public string Drive;
        public bool NeedsUpdate, Updating;
        public double SteamMinutes, Steam2Weeks; public DateTime? SteamLast;
        public List<Game> Alts = new List<Game>();   // the same game from another store (merged into one card)
        public Game Parent;                          // set on an alt: the card it belongs to
        public string SourceName { get { return Source == "steam" ? "Steam" : Source == "battlenet" ? "Battle.net" : Source == "epic" ? "Epic Games" : "Added by you"; } }
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
        [OnDeserializing] void D(StreamingContext c) { LastPlayed = ""; Preset = ""; Days = new List<string>(); PlayOn = ""; }

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
            var cutoff = when.Date.AddDays(-13).ToString("yyyy-MM-dd");
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
        [OnDeserializing] void D(StreamingContext c)
        {
            Games = new Dictionary<string, GameData>(); Custom = new List<Game>(); Sort = "recent"; Filter = "all"; Selected = ""; TrayOnClose = true; WinW = -1; WinH = -1; LastSeenVersion = "";
        }

        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HallowHub"); } }
        static string FilePath { get { return Path.Combine(Dir, "hub.json"); } }
        public static bool Frozen;   // snapshots: never write

        public GameData For(string id)
        {
            GameData g; if (!Games.TryGetValue(id, out g)) { g = new GameData(); Games[id] = g; } return g;
        }

        public static HubSettings Load()
        {
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
