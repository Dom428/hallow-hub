// Hallow Hub v2.7 - library backup: Hub's own data (collections, notes, pins, hidden games, your names and pictures,
// settings, playtime and session history) in one zip, and import on another PC - games are matched by store id, then name.
// Nothing secret is in it: Hub keeps no passwords, keys or webhooks (Discord goes through Hallow Toolkit's own link).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace HallowHub
{
    [DataContract]
    public class LibraryBackup
    {
        [DataMember] public string Format = "hallow-hub-library";
        [DataMember] public int Version = 1;
        [DataMember] public string HubVersion = "", Exported = "";
        [DataMember] public Dictionary<string, GameData> Games = new Dictionary<string, GameData>();
        [DataMember] public Dictionary<string, string> Names = new Dictionary<string, string>();   // id -> the game's store name (to match on another PC)
        [DataMember] public List<string> Collections = new List<string>();
        [DataMember] public List<Game> Custom = new List<Game>();
        [DataMember] public HubSettings Settings;   // the settings part only: games, custom games and this-PC things are emptied

        static DataContractJsonSerializerSettings Opts() { return new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }; }
        public byte[] ToJson() { using (var m = new MemoryStream()) { new DataContractJsonSerializer(typeof(LibraryBackup), Opts()).WriteObject(m, this); return m.ToArray(); } }
        public static LibraryBackup FromJson(Stream s) { return (LibraryBackup)new DataContractJsonSerializer(typeof(LibraryBackup), Opts()).ReadObject(s); }
    }

    public static class LibraryZip
    {
        static DataContractJsonSerializerSettings Opts() { return new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }; }
        static HubSettings Clone(HubSettings s)
        {
            using (var m = new MemoryStream())
            {
                var ser = new DataContractJsonSerializer(typeof(HubSettings), Opts()); ser.WriteObject(m, s); m.Position = 0;
                return (HubSettings)ser.ReadObject(m);
            }
        }

        // names: id -> name of every game Hub sees now. Returns how many games went in.
        public static int Export(HubSettings S, Dictionary<string, string> names, string zipPath)
        {
            var b = new LibraryBackup { HubVersion = Program.Version, Exported = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") };
            var copy = Clone(S);
            var art = new Dictionary<string, string>();   // file on this PC -> name in the zip
            foreach (var kv in copy.Games)
            {
                var d = kv.Value;
                d.CustomCover = ArtName(d.CustomCover, art); d.CustomHero = ArtName(d.CustomHero, art);
                d.OpenWith = new List<string>(d.OpenWith);   // app paths are kept: they may exist on the other PC too
                b.Games[kv.Key] = d;
                string n; if (names.TryGetValue(kv.Key, out n)) b.Names[kv.Key] = n;
            }
            b.Collections = copy.Collections.ToList();
            b.Custom = copy.Custom.ToList();
            // the settings, without the library itself and this PC's own bits
            copy.Games = new Dictionary<string, GameData>(); copy.Custom = new List<Game>(); copy.Collections = new List<string>();
            copy.Selected = ""; copy.WinW = -1; copy.WinH = -1; copy.LastSeenVersion = ""; copy.LastUpdateCheck = ""; copy.LastRecap = ""; copy.LastCrashShown = ""; copy.LastSaleCheck = ""; copy.SaleTold = new List<string>();
            b.Settings = copy;
            var tmp = zipPath + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var z = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                var e = z.CreateEntry("library.json"); using (var w = e.Open()) { var j = b.ToJson(); w.Write(j, 0, j.Length); }
                foreach (var a in art) { try { z.CreateEntryFromFile(a.Key, a.Value); } catch { } }
                var r = z.CreateEntry("README.txt"); using (var w = new StreamWriter(r.Open(), new UTF8Encoding(false)))
                    w.Write("Hallow Hub library backup (" + b.Exported + ", Hallow Hub " + Program.Version + ").\r\nIn Hallow Hub: Settings > Import library... and pick this zip.\r\nIt holds your collections, notes, pins, hidden games, names and pictures, settings and playtime history - no passwords or keys.\r\n");
            }
            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(tmp, zipPath);
            return b.Games.Count;
        }
        static string ArtName(string path, Dictionary<string, string> art)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
            string n; if (art.TryGetValue(path, out n)) return n;
            n = "art/" + art.Count + Path.GetExtension(path).ToLowerInvariant(); art[path] = n; return n;
        }

        public static LibraryBackup Read(string zipPath, out Dictionary<string, byte[]> artFiles)
        {
            artFiles = new Dictionary<string, byte[]>();
            using (var z = ZipFile.OpenRead(zipPath))
            {
                var e = z.GetEntry("library.json"); if (e == null) throw new InvalidDataException("This zip isn't a Hallow Hub library backup.");
                LibraryBackup b; using (var s = e.Open()) b = LibraryBackup.FromJson(s);
                if (b == null || b.Format != "hallow-hub-library") throw new InvalidDataException("This zip isn't a Hallow Hub library backup.");
                foreach (var a in z.Entries.Where(x => x.FullName.StartsWith("art/") && x.Length > 0 && x.Length < 20 * 1024 * 1024))
                    using (var s = a.Open()) using (var m = new MemoryStream()) { s.CopyTo(m); artFiles[a.FullName] = m.ToArray(); }
                return b;
            }
        }
    }

    public partial class HubWindow
    {
        void ExportLibrary()
        {
            var dlg = new SaveFileDialog { Title = "Back up your Hallow Hub library", Filter = "Zip (*.zip)|*.zip", FileName = "Hallow Hub library " + DateTime.Now.ToString("yyyy-MM-dd") + ".zip" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var names = AllCopies().GroupBy(c => c.Id).ToDictionary(x => x.Key, x => x.First().OriginalName ?? x.First().Name);
                S.Save();
                int n = LibraryZip.Export(S, names, dlg.FileName);
                Status("Library backed up - " + n + " games' data in " + Path.GetFileName(dlg.FileName) + ".");
            }
            catch (Exception ex) { Status("Couldn't back up the library: " + ex.Message); }
        }

        void ImportLibrary()
        {
            var dlg = new OpenFileDialog { Title = "Import a Hallow Hub library backup", Filter = "Zip (*.zip)|*.zip", CheckFileExists = true };
            if (dlg.ShowDialog(this) != true) return;
            LibraryBackup b; Dictionary<string, byte[]> art;
            try { b = LibraryZip.Read(dlg.FileName, out art); }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't read that backup: " + ex.Message, "Import library", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            int matched = b.Games.Keys.Count(id => MatchLocal(id, b) != null);
            if (MessageBox.Show(this, "Import the library from " + (b.Exported.Length >= 10 ? b.Exported.Substring(0, 10) : "the backup") + "?\n\n" +
                b.Games.Count + " games' data (" + matched + " of them are on this PC), " + b.Collections.Count + " collections, and the settings.\n\n" +
                "Your collections, notes, pins and playtime here are kept - the backup is added to them. Hub saves a copy of your current data first.",
                "Import library", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                var f = Path.Combine(HubSettings.Dir, "hub.json"); if (File.Exists(f)) File.Copy(f, f + ".before-import", true);
                int n = MergeBackup(b, art, Path.Combine(HubSettings.Dir, "art"));
                S.Save(); imgCache.Clear(); accentCache.Clear(); HallowLook.UseShared = S.UseHallowAccent;
                Rescan(); ApplyAccent(true);
                Status("Library imported - " + n + " games got their data back.");
            }
            catch (Exception ex) { Status("Couldn't import the library: " + ex.Message); }
        }

        // a game on this PC for a backup entry: the same store id, else the same name
        Game MatchLocal(string id, LibraryBackup b)
        {
            var all = AllCopies().ToList();
            var g = all.FirstOrDefault(x => x.Id == id); if (g != null) return Main(g);
            string n; if (!b.Names.TryGetValue(id, out n) || string.IsNullOrEmpty(n)) return null;
            var k = Library.Norm(n);
            g = all.FirstOrDefault(x => Library.Norm(x.OriginalName ?? x.Name) == k || Library.Norm(x.Name) == k);
            return g == null ? null : Main(g);
        }

        // adds the backup to what's here (nothing here is thrown away); returns how many games on this PC got data
        int MergeBackup(LibraryBackup b, Dictionary<string, byte[]> art, string artDir)
        {
            int n = 0;
            Func<string, string> artPath = name =>
            {
                byte[] bytes; if (string.IsNullOrEmpty(name) || !art.TryGetValue(name, out bytes)) return "";
                Directory.CreateDirectory(artDir);
                var p = Path.Combine(artDir, "imported-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Path.GetFileName(name));
                File.WriteAllBytes(p, bytes); return p;
            };
            foreach (var kv in b.Games)
            {
                var local = MatchLocal(kv.Key, b);
                var id = local != null ? local.Id : kv.Key;   // not on this PC: kept under its id, used when it's installed
                if (local != null) n++;
                var from = kv.Value; var to = S.For(id);
                to.Minutes = Math.Max(to.Minutes, from.Minutes); to.Sessions = Math.Max(to.Sessions, from.Sessions);
                var lt = to.Last; var lf = from.Last; if (lf != null && (lt == null || lf > lt)) to.LastPlayed = from.LastPlayed;
                to.Hidden |= from.Hidden; to.Favorite |= from.Favorite; to.Pinned |= from.Pinned; to.GameSession |= from.GameSession; to.KeepOpen |= from.KeepOpen; to.Admin |= from.Admin;
                foreach (var t in from.Tags) if (!to.Tags.Contains(t)) to.Tags.Add(t);
                foreach (var o in from.OpenWith) if (!to.OpenWith.Contains(o, StringComparer.OrdinalIgnoreCase)) to.OpenWith.Add(o);
                foreach (var o in from.Links ?? new List<string>()) if (!to.Links.Contains(o, StringComparer.OrdinalIgnoreCase)) to.Links.Add(o);   // v2.12
                foreach (var o in from.CloseFirst ?? new List<string>()) if (CloseApps.Allowed(o) && !to.CloseFirst.Contains(o, StringComparer.OrdinalIgnoreCase)) to.CloseFirst.Add(o);
                to.NoReopen |= from.NoReopen;
                if (to.Preset == "") to.Preset = from.Preset ?? "";
                if ((to.Status ?? "") == "") to.Status = from.Status ?? ""; if (to.Rating == 0) to.Rating = from.Rating;
                if (to.LaunchArgs == "") to.LaunchArgs = from.LaunchArgs ?? "";
                if (to.DailyLimit == 0) to.DailyLimit = from.DailyLimit;
                if (to.CustomName == "") to.CustomName = from.CustomName ?? "";
                if (to.CustomCover == "") to.CustomCover = artPath(from.CustomCover);
                if (to.CustomHero == "") to.CustomHero = artPath(from.CustomHero);
                var fn = (from.Notes ?? "").Trim(); var tn = (to.Notes ?? "").Trim();
                if (fn.Length > 0 && !tn.Contains(fn)) to.Notes = tn.Length == 0 ? fn : tn + "\n\n" + fn;
                // day by day: the bigger of the two counts for each day
                var days = new Dictionary<string, double>();
                foreach (var d in to.Days.Concat(from.Days)) { var p = d.Split('='); double v; if (p.Length == 2 && double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) { double cur; days[p[0]] = days.TryGetValue(p[0], out cur) ? Math.Max(cur, v) : v; } }
                to.Days = days.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).ToList();
                to.SessionLog = to.SessionLog.Concat(from.SessionLog).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
                while (to.SessionLog.Count > 40) to.SessionLog.RemoveAt(0);
                if (to.PlayOn == "" && local != null && new[] { local }.Concat(local.Alts).Any(c => c.Id == from.PlayOn)) to.PlayOn = from.PlayOn;
            }
            foreach (var c in b.Collections) if (!S.Collections.Contains(c)) S.Collections.Add(c);
            foreach (var c in b.Custom) if (c != null && !string.IsNullOrEmpty(c.Launch) && File.Exists(c.Launch) && !S.Custom.Any(x => x.Id == c.Id)) S.Custom.Add(c);
            var st = b.Settings;
            if (st != null)
            {
                S.Sort = st.Sort; S.View = st.View; S.CardScale = st.CardScale; S.StillBanner = st.StillBanner; S.BreakMinutes = st.BreakMinutes; S.WeeklyRecap = st.WeeklyRecap;
                S.ShowHidden = st.ShowHidden; S.TrayOnClose = st.TrayOnClose; S.OneIcon = st.OneIcon; S.UseHallowAccent = st.UseHallowAccent;
                S.DiscordOnStart = st.DiscordOnStart; S.DiscordRecap = st.DiscordRecap;
                S.FStatus = st.FStatus ?? ""; S.FStores = st.FStores ?? new List<string>(); S.FSize = st.FSize ?? ""; S.FLast = st.FLast ?? ""; S.FTime = st.FTime ?? "";
                S.SaleAlerts = st.SaleAlerts; if (string.IsNullOrEmpty(S.SteamProfile)) S.SteamProfile = st.SteamProfile ?? "";
                if (st.SaleTargets != null) foreach (var t in st.SaleTargets) if (!S.SaleTargets.ContainsKey(t.Key)) S.SaleTargets[t.Key] = t.Value;
            }
            return n;
        }
    }
}
