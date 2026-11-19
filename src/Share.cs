// Hallow Hub v1.8 - share: a Discord post when you start a game (opt-in, through Hallow Toolkit's Discord link),
// a weekly recap, play streaks, and exporting your playtime to a spreadsheet.
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
using Microsoft.Win32;

namespace HallowHub
{
    public static class Discord
    {
        // Hallow Toolkit's Discord link (Toolkit > More > Discord). Read only when posting; never shown or logged.
        static string Webhook()
        {
            try
            {
                var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"HallowToolkit\settings.json");
                if (!File.Exists(f)) return null;
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                object u; if (!d.TryGetValue("DiscordWebhook", out u)) return null;
                var url = u as string;
                return url != null && Regex.IsMatch(url, @"^https://(canary\.|ptb\.)?discord(app)?\.com/api/webhooks/\d+/[\w-]+$") ? url : null;
            }
            catch { return null; }
        }
        public static bool Connected { get { return Webhook() != null; } }
        public static void Post(string title, string text, int color)
        {
            var url = Webhook(); if (url == null) return;
            new Thread(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    var body = new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                        { "username", "Hallow Hub" },
                        { "embeds", new[] { new Dictionary<string, object> { { "title", title }, { "description", text }, { "color", color } } } } });
                    var req = (HttpWebRequest)WebRequest.Create(url); req.Method = "POST"; req.ContentType = "application/json"; req.UserAgent = "HallowHub"; req.Timeout = 8000;
                    var b = Encoding.UTF8.GetBytes(body); using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length);
                    using (req.GetResponse()) { }
                }
                catch { }
            }) { IsBackground = true }.Start();
        }
    }

    public partial class HubWindow
    {
        static int Rgb(System.Windows.Media.Color c) { return (c.R << 16) | (c.G << 8) | c.B; }

        void ShareStart(Game g)
        {
            if (!S.DiscordOnStart) return;
            var t = Total(g);
            Discord.Post("Playing " + g.Name, (t >= 60 ? HubSettings.Duration(t) + " played so far" : "Just getting started") + " · " + PlayCopy(g).SourceName, Rgb(Accent(g)));
        }

        // days in a row with some play (today counts once you've played; otherwise the streak up to yesterday)
        int Streak()
        {
            Func<DateTime, bool> played = day => S.Games.Values.Any(x => x.MinutesOn(day) >= 1);
            var d = DateTime.Today; if (!played(d)) d = d.AddDays(-1);
            int n = 0; while (played(d) && n < 400) { n++; d = d.AddDays(-1); }
            return n;
        }

        // Monday-based weeks
        static DateTime WeekStart(DateTime day) { int off = ((int)day.DayOfWeek + 6) % 7; return day.Date.AddDays(-off); }
        void CheckRecap()
        {
            if (!S.WeeklyRecap) return;
            var thisWeek = WeekStart(DateTime.Today).ToString("yyyy-MM-dd");
            if (S.LastRecap == thisWeek) return;
            if (string.IsNullOrEmpty(S.LastRecap)) { S.LastRecap = thisWeek; S.Save(); return; }   // first time: start counting from now
            S.LastRecap = thisWeek; S.Save();
            var from = WeekStart(DateTime.Today).AddDays(-7);
            var per = games.Select(g => Tuple.Create(g, Enumerable.Range(0, 7).Sum(i => S.For(g.Id).MinutesOn(from.AddDays(i))))).Where(x => x.Item2 >= 1).OrderByDescending(x => x.Item2).ToList();
            if (per.Count == 0) return;
            var total = per.Sum(x => x.Item2);
            var text = HubSettings.Duration(total) + " last week, mostly " + per[0].Item1.Name + " (" + HubSettings.Duration(per[0].Item2) + ")" +
                (per.Count > 1 ? ", then " + string.Join(", ", per.Skip(1).Take(2).Select(x => x.Item1.Name)) : "") + "." + (Streak() >= 2 ? " Streak: " + Streak() + " days." : "");
            Notify("Your week in games", text);
            if (S.DiscordRecap) Discord.Post("My week in games", text, 0xF07820);
        }

        // two files: one row per game, and one row per session
        void ExportCsv()
        {
            var dlg = new SaveFileDialog { Title = "Export your playtime", Filter = "Spreadsheet (*.csv)|*.csv", FileName = "Hallow Hub playtime " + DateTime.Today.ToString("yyyy-MM-dd") + ".csv" };
            if (dlg.ShowDialog(this) != true) return;
            Func<string, string> q = v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
            Func<double, string> n = v => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                var sb = new StringBuilder("Game,Store,Hours played,Hours last 2 weeks,Last played,Sessions,Size (GB),Favorite,Collections\r\n");
                foreach (var g in games.OrderByDescending(x => Total(x)))
                {
                    var d = S.For(g.Id); var last = LastOf(g);
                    sb.Append(string.Join(",", q(g.Name), q(g.SourceName + (g.Alts.Count > 0 ? " + " + string.Join(" + ", g.Alts.Select(a => a.SourceName)) : "")),
                        n(Total(g) / 60), n(Recent(g) / 60), last == null ? "" : last.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), d.Sessions.ToString(),
                        n((g.SizeBytes + g.Alts.Sum(a => a.SizeBytes)) / (double)(1L << 30)), d.Favorite ? "yes" : "", q(string.Join("; ", d.Tags)))).Append("\r\n");
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));   // BOM so Excel reads accents right
                var ses = new StringBuilder("Game,Started,Minutes\r\n");
                foreach (var g in games)
                    foreach (var e in S.For(g.Id).SessionLog)
                    {
                        var p = e.Split('|'); DateTime t;
                        if (p.Length == 2 && DateTime.TryParse(p[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out t)) ses.Append(q(g.Name) + "," + t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + "," + p[1] + "\r\n");
                    }
                var f2 = Path.Combine(Path.GetDirectoryName(dlg.FileName), Path.GetFileNameWithoutExtension(dlg.FileName) + " - sessions.csv");
                File.WriteAllText(f2, ses.ToString(), new UTF8Encoding(true));
                Status("Exported to " + Path.GetFileName(dlg.FileName) + " (and a sessions file next to it).");
            }
            catch (Exception ex) { Status("Couldn't export: " + ex.Message); }
        }
    }
}
