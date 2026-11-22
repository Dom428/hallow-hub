// Hallow Hub v2.18 - switch Steam accounts. Lists the accounts Steam remembers on this PC
// (Steam\config\loginusers.vdf) and switches with one click: Steam is closed (steam.exe -shutdown),
// the picked account is set as the one to sign in (HKCU\Software\Valve\Steam AutoLoginUser, the
// same value Steam's own "Change account" writes) and Steam is opened again. Hub never sees, types
// or stores a password: an account Steam doesn't remember the password for just shows Steam's
// sign-in. Not while a game is running.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace HallowHub
{
    public static class SteamAccounts
    {
        public class Account { public string SteamId64, AccountName, Persona; public bool Remembered; public long LastUsed; }

        const ulong Base = 76561197960265728UL;

        /// <summary>The accounts Steam remembers on this PC, most recently used first.</summary>
        public static List<Account> List()
        {
            var list = new List<Account>();
            var steam = Library.SteamPath(); if (steam == null) return list;
            var file = Path.Combine(steam, @"config\loginusers.vdf"); if (!File.Exists(file)) return list;
            try
            {
                var users = Vdf.Path(Vdf.Parse(File.ReadAllText(file, Encoding.UTF8)), "users") as Dictionary<string, object>;
                if (users == null) return list;
                foreach (var kv in users)
                {
                    var u = kv.Value as Dictionary<string, object>; if (u == null) continue;
                    var name = Vdf.Str(u, "AccountName"); if (string.IsNullOrEmpty(name)) continue;
                    long ts; long.TryParse(Vdf.Str(u, "Timestamp"), out ts);
                    list.Add(new Account
                    {
                        SteamId64 = kv.Key, AccountName = name, Persona = Vdf.Str(u, "PersonaName") ?? name,
                        Remembered = Vdf.Str(u, "RememberPassword") == "1" || Vdf.Str(u, "AllowAutoLogin") == "1", LastUsed = ts
                    });
                }
            }
            catch { }
            return list.OrderByDescending(a => a.LastUsed).ToList();
        }

        /// <summary>The account signed in now (Steam running), else the one Steam will sign in next.</summary>
        public static Account Current(List<Account> list)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
                {
                    var active = k == null ? null : k.GetValue("ActiveUser");
                    if (active is int && (int)active != 0)
                    {
                        var id = (Base + (uint)(int)active).ToString();
                        var a = list.FirstOrDefault(x => x.SteamId64 == id); if (a != null) return a;
                    }
                }
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var auto = k == null ? null : k.GetValue("AutoLoginUser") as string;
                    if (!string.IsNullOrEmpty(auto)) return list.FirstOrDefault(x => string.Equals(x.AccountName, auto, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch { }
            return null;
        }

        static string SteamExe()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var exe = k == null ? null : k.GetValue("SteamExe") as string;
                    if (!string.IsNullOrEmpty(exe) && File.Exists(exe.Replace('/', '\\'))) return exe.Replace('/', '\\');
                }
            }
            catch { }
            var p = Library.SteamPath(); if (p == null) return null;
            var e = Path.Combine(p, "steam.exe"); return File.Exists(e) ? e : null;
        }

        static bool SteamRunning() { return Process.GetProcessesByName("steam").Length > 0; }

        /// <summary>Closes Steam, sets the account to sign in (null: Steam's own account picker), opens Steam.
        /// Runs on a background thread; progress and the result go to report (on that thread).</summary>
        public static void Switch(Account to, Action<string> report, Action<bool> done)
        {
            var exe = SteamExe();
            if (exe == null) { report("Steam isn't installed on this PC."); done(false); return; }
            new Thread(() =>
            {
                bool ok = false;
                try
                {
                    if (SteamRunning())
                    {
                        report("Closing Steam...");
                        Process.Start(new ProcessStartInfo(exe, "-shutdown") { UseShellExecute = false });
                        var until = DateTime.Now.AddSeconds(40);
                        while (SteamRunning() && DateTime.Now < until) Thread.Sleep(500);
                        if (SteamRunning()) { report("Steam didn't close in time. Close it yourself, then try again."); done(false); return; }
                        Thread.Sleep(1500);   // let it finish writing its files
                    }
                    using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Valve\Steam"))
                    {
                        // Steam's own "Change account" sets these two: who to sign in, and to use their saved sign-in.
                        k.SetValue("AutoLoginUser", to == null ? "" : to.AccountName, RegistryValueKind.String);
                        k.SetValue("RememberPassword", to == null ? 0 : 1, RegistryValueKind.DWord);
                    }
                    report(to == null ? "Opening Steam to sign in with another account..." : "Opening Steam as " + to.Persona + "...");
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                    ok = true;
                }
                catch (Exception ex) { report("Couldn't switch Steam accounts: " + ex.Message); }
                done(ok);
            }) { IsBackground = true, Name = "Steam account switch" }.Start();
        }
    }

    public partial class HubWindow
    {
        Button steamAccountBtn;
        bool steamSwitching;

        Button BuildSteamAccountButton()
        {
            steamAccountBtn = new Button { Content = "Steam account", Tag = "icon", Width = 36, Height = 34, Padding = new Thickness(0), Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Switch Steam accounts" };
            steamAccountBtn.Click += (s, e) =>
            {
                var menu = new ContextMenu();
                foreach (var it in SteamAccountChoices()) menu.Items.Add(it);
                menu.PlacementTarget = steamAccountBtn; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true;
            };
            steamAccountBtn.Visibility = Library.SteamPath() == null ? Visibility.Collapsed : Visibility.Visible;
            return steamAccountBtn;
        }

        // The accounts as menu entries (the window's button).
        List<object> SteamAccountChoices()
        {
            var items = new List<object>();
            var list = SteamAccounts.List(); var cur = SteamAccounts.Current(list);
            items.Add(new MenuItem { Header = cur != null ? "Signed in: " + cur.Persona : "Steam accounts on this PC", IsEnabled = false });
            foreach (var a in list)
            {
                var acc = a;
                var mi = new MenuItem { Header = a.Persona + "  (" + a.AccountName + ")" + (a.Remembered ? "" : "  - asks for the password"), IsCheckable = false, IsChecked = cur != null && cur.SteamId64 == a.SteamId64 };
                mi.Click += (s, e) => SwitchSteamAccount(acc);
                items.Add(mi);
            }
            items.Add(new Separator());
            var other = new MenuItem { Header = "Sign in with another account..." };
            other.Click += (s, e) => SwitchSteamAccount(null);
            items.Add(other);
            return items;
        }

        // The same in the tray icon's menu.
        WF.ToolStripMenuItem traySteam;
        void UpdateTraySteamMenu(WF.ContextMenuStrip menu)
        {
            if (Library.SteamPath() == null) return;
            if (traySteam == null)
            {
                traySteam = new WF.ToolStripMenuItem("Steam account");
                var at = Math.Max(0, menu.Items.IndexOf(quitItem) - 1);
                menu.Items.Insert(at, traySteam);
            }
            traySteam.DropDownItems.Clear();
            var list = SteamAccounts.List(); var cur = SteamAccounts.Current(list);
            foreach (var a in list)
            {
                var acc = a;
                var mi = new WF.ToolStripMenuItem(a.Persona + "  (" + a.AccountName + ")") { Checked = cur != null && cur.SteamId64 == a.SteamId64 };
                mi.Click += (s, e) => Dispatcher.BeginInvoke(new Action(() => SwitchSteamAccount(acc)));
                traySteam.DropDownItems.Add(mi);
            }
            if (list.Count > 0) traySteam.DropDownItems.Add(new WF.ToolStripSeparator());
            var other = new WF.ToolStripMenuItem("Sign in with another account...");
            other.Click += (s, e) => Dispatcher.BeginInvoke(new Action(() => SwitchSteamAccount(null)));
            traySteam.DropDownItems.Add(other);
        }

        void SwitchSteamAccount(SteamAccounts.Account to)
        {
            if (steamSwitching) return;
            if (tracker.Now.Count > 0) { Status("Close your game first - switching accounts closes Steam."); return; }
            var list = SteamAccounts.List(); var cur = SteamAccounts.Current(list);
            if (to != null && cur != null && cur.SteamId64 == to.SteamId64 && Process.GetProcessesByName("steam").Length > 0) { Status("You're already signed in as " + to.Persona + "."); return; }
            steamSwitching = true;
            SteamAccounts.Switch(to, msg => Dispatcher.BeginInvoke(new Action(() => Status(msg))), ok => Dispatcher.BeginInvoke(new Action(() =>
            {
                steamSwitching = false;
                if (!ok) return;
                // Sale alerts followed the old account: follow the new one too.
                if (to != null && cur != null && (S.SteamProfile ?? "").Trim() == cur.SteamId64) { S.SteamProfile = to.SteamId64; S.LastSaleCheck = ""; S.Save(); }
                // Steam hours and last played come from the signed-in account: read them again once Steam is up.
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
                t.Tick += (s, e) => { t.Stop(); RescanInBackground(() => Status(to == null ? "Steam is open - pick an account there." : "Now using " + to.Persona + "'s Steam hours.")); };
                t.Start();
            })));
        }
    }
}
