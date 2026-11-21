// Hallow Hub v2.9 - emulators and ROMs: your own emulator profiles (the emulator's .exe, its arguments with {rom}, a ROM folder
// and the file types to look for). Every ROM becomes a game in the library - playtime, collections, notes, Big Picture -
// started through its emulator. Nothing is downloaded and nothing in the ROM folders is changed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WF = System.Windows.Forms;

namespace HallowHub
{
    [DataContract]
    public class Emulator
    {
        [DataMember] public string Name = "", Exe = "", Args = "\"{rom}\"", Folder = "", Types = "";   // Types: "sfc smc zip"
        [DataMember] public bool Subfolders = true;
        [OnDeserializing] void D(StreamingContext c) { Name = ""; Exe = ""; Args = "\"{rom}\""; Folder = ""; Types = ""; Subfolders = true; }
        public IEnumerable<string> Extensions { get { return (Types ?? "").Split(new[] { ' ', ',', ';', '.' }, StringSplitOptions.RemoveEmptyEntries).Select(x => "." + x.Trim().ToLowerInvariant()).Distinct(); } }
    }

    public static class Roms
    {
        // the emulator's arguments for one ROM: {rom} = full path, {name} = file name without extension, {folder} = its folder
        public static string ArgsFor(string template, string rom)
        {
            var t = string.IsNullOrWhiteSpace(template) ? "\"{rom}\"" : template;
            if (!t.Contains("{rom}") && !t.Contains("{name}")) t = t.TrimEnd() + " \"{rom}\"";
            return t.Replace("{rom}", rom).Replace("{name}", Path.GetFileNameWithoutExtension(rom)).Replace("{folder}", Path.GetDirectoryName(rom));
        }
        // "Super Game (USA) (Rev 1) [!]" -> "Super Game"
        public static string CleanName(string file)
        {
            var n = Path.GetFileNameWithoutExtension(file);
            n = Regex.Replace(n, @"\s*[\(\[][^\)\]]*[\)\]]", "");
            n = n.Replace('_', ' ').Trim();
            return n.Length == 0 ? Path.GetFileNameWithoutExtension(file) : n;
        }
        static readonly string[] Pics = { ".png", ".jpg", ".jpeg" };
        // a cover next to the ROM (same name), or in a covers / boxart / images folder beside it
        static string CoverFor(string rom)
        {
            var dir = Path.GetDirectoryName(rom); var n = Path.GetFileNameWithoutExtension(rom);
            foreach (var sub in new[] { "", "covers", "boxart", "box art", "images", "media" })
                foreach (var ext in Pics) { var f = Path.Combine(sub.Length == 0 ? dir : Path.Combine(dir, sub), n + ext); if (File.Exists(f)) return f; }
            return null;
        }

        // every ROM of every profile (at most 3000 per profile, one folder level down when Subfolders is on)
        public static List<Game> Scan(List<Emulator> emus)
        {
            var r = new List<Game>();
            foreach (var e in emus ?? new List<Emulator>())
            {
                try
                {
                    if (string.IsNullOrEmpty(e.Folder) || !Directory.Exists(e.Folder) || string.IsNullOrEmpty(e.Exe)) continue;
                    var exts = new HashSet<string>(e.Extensions); if (exts.Count == 0) continue;
                    var files = new List<string>();
                    files.AddRange(Directory.GetFiles(e.Folder));
                    if (e.Subfolders) foreach (var d in Directory.GetDirectories(e.Folder)) { try { files.AddRange(Directory.GetFiles(d)); } catch { } }
                    foreach (var f in files.Where(x => exts.Contains(Path.GetExtension(x).ToLowerInvariant())).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(3000))
                    {
                        long size = 0; try { size = new FileInfo(f).Length; } catch { }
                        r.Add(new Game { Id = "rom:" + f.ToLowerInvariant(), Name = CleanName(f), Source = "rom", Platform = e.Name, RomPath = f, InstallDir = null,
                            Launch = e.Exe, Args = ArgsFor(e.Args, f), IconExe = File.Exists(e.Exe) ? e.Exe : null, Cover = CoverFor(f), SizeBytes = size,
                            Drive = Path.GetPathRoot(f).TrimEnd('\\'), UpdatedAt = File.GetLastWriteTime(f) });
                    }
                }
                catch { }
            }
            return r;
        }
    }

    // Settings > Emulators...: your emulator profiles
    public class EmulatorsWindow : Window
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(HallowLook.Hex(hex))); }
        readonly List<Emulator> list; readonly StackPanel rows = new StackPanel();
        public List<Emulator> Result;

        public EmulatorsWindow(List<Emulator> current)
        {
            list = current.Select(e => new Emulator { Name = e.Name, Exe = e.Exe, Args = e.Args, Folder = e.Folder, Types = e.Types, Subfolders = e.Subfolders }).ToList();
            Title = "Emulators - Hallow Hub"; Width = 640; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text"); FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
            HallowLook.Dress(this);   // v2.15: the title bar follows light / dark
            var sp = new StackPanel { Margin = new Thickness(24, 18, 24, 20) };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var hi = Icons.Make("compact-disc", 16, B("#F07820")); hi.Margin = new Thickness(0, 0, 10, 0); head.Children.Add(hi);
            head.Children.Add(new TextBlock { Text = "Emulators", FontSize = 17, FontWeight = FontWeights.Bold });
            sp.Children.Add(head);
            sp.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, Margin = new Thickness(0, 0, 0, 12), LineHeight = 17,
                Text = "Each emulator gets a ROM folder and the file types to look for; every ROM shows up as a game. Arguments use {rom} for the ROM's path ({name} and {folder} work too). " +
                    "Examples - RetroArch: -L cores\\snes9x_libretro.dll \"{rom}\"   Dolphin: -b -e \"{rom}\"   PCSX2: -batch \"{rom}\"" });
            sp.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var add = new Button { Content = "Add emulator", Style = (Style)FindResource("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            add.Click += (o, e) => { list.Add(new Emulator { Name = "Emulator " + (list.Count + 1) }); Fill(); };
            sp.Children.Add(add);
            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Cancel", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0) }; cancel.Click += (o, e) => { DialogResult = false; };
            var save = new Button { Content = "Save", Style = (Style)FindResource("Accent"), MinWidth = 90 };
            save.Click += (o, e) => { Result = list.Where(x => x.Exe.Trim().Length > 0 || x.Folder.Trim().Length > 0).ToList(); DialogResult = true; };
            foot.Children.Add(cancel); foot.Children.Add(save); sp.Children.Add(foot);
            Content = sp; Fill();
        }

        void Fill()
        {
            rows.Children.Clear();
            if (list.Count == 0) rows.Children.Add(new TextBlock { Text = "No emulators yet - Add emulator to start.", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 4, 0, 4) });
            foreach (var em in list)
            {
                var e0 = em;
                var card = new Border { Background = B("#211B2A"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 0, 0, 10), BorderBrush = B("#3A3248"), BorderThickness = new Thickness(1) };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                int row = 0;
                Func<string, string, Action<string>, string, TextBox> field = (label, value, set, browse) =>
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 6) };
                    Grid.SetRow(l, row); g.Children.Add(l);
                    var t = new TextBox { Style = (Style)FindResource("Field"), Text = value ?? "", Margin = new Thickness(0, 0, 0, 6) };
                    t.TextChanged += (o, e) => set(t.Text);
                    Grid.SetRow(t, row); Grid.SetColumn(t, 1); g.Children.Add(t);
                    if (browse != null)
                    {
                        var b = new Button { Content = "Browse...", Style = (Style)FindResource("Btn"), Margin = new Thickness(6, 0, 0, 6), Padding = new Thickness(10, 3, 10, 3) };
                        b.Click += (o, e) =>
                        {
                            if (browse == "exe") { var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Pick the emulator's .exe", Filter = "Programs (*.exe)|*.exe" }; if (dlg.ShowDialog(this) == true) t.Text = dlg.FileName; }
                            else { using (var fb = new WF.FolderBrowserDialog { Description = "Pick the folder with your ROMs" }) if (fb.ShowDialog() == WF.DialogResult.OK) t.Text = fb.SelectedPath; }
                        };
                        Grid.SetRow(b, row); Grid.SetColumn(b, 2); g.Children.Add(b);
                    }
                    row++; return t;
                };
                field("Name / system", e0.Name, v => e0.Name = v, null);
                field("Emulator .exe", e0.Exe, v => e0.Exe = v, "exe");
                field("Arguments", e0.Args, v => e0.Args = v, null);
                field("ROM folder", e0.Folder, v => e0.Folder = v, "folder");
                field("File types", e0.Types, v => e0.Types = v, null);
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var bottom = new DockPanel();
                var rm = new Button { Content = "Remove", Style = (Style)FindResource("Btn"), Padding = new Thickness(10, 3, 10, 3) };
                rm.Click += (o, e) => { list.Remove(e0); Fill(); };
                DockPanel.SetDock(rm, Dock.Right); bottom.Children.Add(rm);
                var sub = new CheckBox { Content = new TextBlock { Text = "Also look one folder down" }, IsChecked = e0.Subfolders, Style = (Style)FindResource("Switch"), Margin = new Thickness(0) };
                sub.Click += (o, e) => e0.Subfolders = sub.IsChecked == true;
                bottom.Children.Add(sub);
                Grid.SetRow(bottom, row); Grid.SetColumnSpan(bottom, 3); g.Children.Add(bottom);
                card.Child = g; rows.Children.Add(card);
            }
        }
    }
}
