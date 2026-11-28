// Hallow Hub - Font Awesome icons (path data in Icons.g.cs). Buttons get their icon from their label,
// so a label that changes ("Mute" -> "Muted") changes its icon too.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HallowHub
{
    public static partial class Icons
    {
        static readonly Dictionary<string, Geometry> cache = new Dictionary<string, Geometry>();

        // exact label -> icon
        static readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "Play", "play" }, { "Add game", "plus" }, { "Refresh", "arrows-rotate" }, { "Settings", "gear" }, { "Stats", "chart-line" }, { "Surprise me", "dice" }, { "Game settings", "sliders" }, { "Save", "check" }, { "Add", "plus" }, { "Shortcuts", "keyboard" }, { "Check for updates", "arrows-rotate" }, { "Storage", "hard-drive" }, { "Cover picture...", "image" }, { "Banner picture...", "image" }, { "Reset look", "arrows-rotate" }, { "Add app...", "plus" }, { "Notes", "note-sticky" }, { "Export", "file-csv" }, { "Big Picture", "tv" }, { "Open Hallow Hub", "window-restore" }, { "Grid view", "table-cells-large" }, { "List view", "list" }, { "EA app", "gamepad" }, { "Ubisoft Connect", "gamepad" }, { "GOG", "gamepad" }, { "Riot", "gamepad" }, { "Xbox", "xbox" }, { "Needs update", "arrows-rotate" }, { "Not played lately", "clock-rotate-left" }, { "x", "xmark" }, { "+", "plus" },
            { "Favorite", "star" }, { "Open folder", "folder-open" }, { "Hide", "eye-slash" }, { "Unhide", "eye" }, { "Remove", "trash-can" },
            { "All", "table-cells-large" }, { "Favorites", "star" }, { "Steam", "steam" }, { "Battle.net", "battle-net" }, { "Epic Games", "gamepad" }, { "Added by you", "windows" },
            { "Back up now", "floppy-disk" }, { "News", "newspaper" }, { "About", "circle-info" }, { "Emulators...", "compact-disc" }, { "Emulators", "compact-disc" }, { "Add emulator", "plus" }, { "Browse...", "folder-open" }, { "Wishlist", "cart-shopping" }, { "Steam account", "steam" }, { "Check now", "arrows-rotate" }, { "Back up library...", "file-export" }, { "Import library...", "file-import" }, { "Read more", "arrow-up-right-from-square" }, { "Show details", "note-sticky" }, { "Backing up...", "spinner" }, { "Restore...", "rotate-left" },
            { "OK", "check" }, { "Cancel", "xmark" }, { "Got it", "check" }, { "Store page", "arrow-up-right-from-square" },
            { "Links", "bookmark" }, { "Add link", "plus" }, { "Add a running app", "power-off" },
            { "Family", "people-group" }, { "Open", "arrow-up-right-from-square" }, { "Install", "download" }, { "Pause all", "pause" }, { "Resume all", "play" },   // v2.17: the Family page
            { "Connect fastest", "bolt" }, { "Disconnect", "power-off" }, { "Lock now", "lock" }, { "Set address...", "location-dot" }, { "Change address...", "pen" }, { "How to set up", "circle-info" },
            { "Capture a region", "camera" }, { "Clipboard history", "clock-rotate-left" }, { "Pause history", "pause" }, { "Resume history", "play" },   // v2.23: Hallow Capture's card
            { "Put windows back", "display" }, { "Night mode", "moon" }, { "Stop game profile", "rotate-left" },                                    // v2.23: Hallow Display's card
            { "Search", "magnifying-glass" }, { "Rebuild index", "arrows-rotate" },                                                                    // v2.24: Hallow Find's card
            { "New window", "plus" }, { "Downloads", "download" },                                                                                    // v2.25: Hallow Files' card
            { "Diagnose", "stethoscope" }, { "New image", "image" }, { "Open a picture...", "folder-open" }, { "New tab", "plus" }, { "Open a file...", "folder-open" }, { "Orphans", "broom" },   // v2.26: Net, Paint, Notes and Uninstall's cards
            { "Crashes", "triangle-exclamation" }, { "PC health", "heart-pulse" }, { "Switch windows", "window-restore" }, { "5-min timer", "hourglass-half" }, { "Dismiss", "bell" }, { "Snooze", "moon" }, { "Duplicates", "layer-group" },   // v2.27: Doctor, Switch, Clock and Fonts' cards (Updates' "Check now" is above)
        };
        // label starts with -> icon
        static readonly string[][] Prefixes = {
            new[] { "Playing", "circle-play" }, new[] { "Update to v", "download" }, new[] { "Updating", "spinner" }, new[] { "Starting", "hourglass-half" }, new[] { "Hidden (", "eye-slash" },
        };
        public static string ForLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            string n; if (Labels.TryGetValue(label.Trim(), out n)) return n;
            foreach (var p in Prefixes) if (label.StartsWith(p[0], StringComparison.OrdinalIgnoreCase)) return p[1];
            return null;
        }

        public static Geometry Get(string name)
        {
            if (name == null) return null;
            Geometry g;
            if (!cache.TryGetValue(name, out g))
            {
                string d; if (!Data.TryGetValue(name, out d)) return null;
                g = Geometry.Parse(d); g.Freeze(); cache[name] = g;
            }
            return g;
        }

        // A standalone icon (strip headers, the header bar).
        public static FrameworkElement Make(string name, double size, Brush fill)
        {
            return new Path { Data = Get(name), Fill = fill, Stretch = Stretch.Uniform, Width = size, Height = size, SnapsToDevicePixels = true };
        }
        // Icon + text for places that aren't styled buttons.
        public static FrameworkElement WithText(string name, string text, double size, Brush fill)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Make(name, size, fill); ic.Margin = new Thickness(0, 0, 7, 0); ic.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(ic); sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }
    }

    // Used by the button styles in Theme.xaml.
    public class IconConv : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) { return Icons.Get(Icons.ForLabel(v as string)); }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
    }
    public class IconVis : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) { return Icons.ForLabel(v as string) != null ? Visibility.Visible : Visibility.Collapsed; }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
    }
    // Space between icon and text (none when the text is hidden).
    public class IconGap : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            var s = v as string;
            return s != null && s.Length > 1 ? new Thickness(0, 0, 7, 0) : new Thickness(0);
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
    }
    // Hides the text of one-character labels that have an icon ("+", "x").
    public class LabelVis : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            var s = v as string;
            return s != null && s.Length == 1 && Icons.ForLabel(s) != null ? Visibility.Collapsed : Visibility.Visible;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
    }
}
