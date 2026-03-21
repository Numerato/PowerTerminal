using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PowerTerminal.Models;

namespace PowerTerminal.Converters
{
    // Shared helper — creates a frozen (immutable, cross-thread) SolidColorBrush.
    internal static class BrushHelper
    {
        internal static SolidColorBrush Freeze(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }

    /// <summary>bool → Visibility (true = Visible)</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is true ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>bool → Visibility (true = Collapsed/Invisible)</summary>
    public class BoolToInvisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is true ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>bool → connected/disconnected colour brush</summary>
    public class BoolToColorBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush Connected    = BrushHelper.Freeze(Color.FromRgb(22,  198,  12));
        private static readonly SolidColorBrush Disconnected = BrushHelper.Freeze(Color.FromRgb(200,  50,  50));

        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is true ? Connected : Disconnected;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>null → false, non-null → true</summary>
    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value != null;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>null → Collapsed, non-null → Visible</summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value != null ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>null → Visible, non-null → Collapsed (for placeholder text)</summary>
    public class NullToVisibilityInvertConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value == null ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>null or empty string → Collapsed, non-empty string → Visible</summary>
    public class NullOrEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>null or empty string → Visible (fallback icon), non-empty → Collapsed</summary>
    public class NullOrEmptyToVisibilityInvertConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>Absolute or relative file path string → BitmapImage (returns null for missing/invalid paths)</summary>
    public class StringToImageSourceConverter : IValueConverter
    {
        private static readonly HashSet<string> AllowedExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".ico", ".bmp", ".gif" };

        public object? Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is not string path || string.IsNullOrEmpty(path)) return null;
            var ext = System.IO.Path.GetExtension(path);
            if (!AllowedExtensions.Contains(ext)) return null;
            try
            {
                // Resolve relative paths (e.g. "ico\linux.ico") to full file-system path
                string fullPath = System.IO.Path.IsPathRooted(path)
                    ? path
                    : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
                var uri = new Uri(fullPath, UriKind.Absolute);
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource   = uri;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch { return null; }
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>null → parameter (first part) or second part of pipe-separated string</summary>
    public class NullToStringConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (p is string param)
            {
                var parts = param.Split('|');
                return value != null ? parts[0] : (parts.Length > 1 ? parts[1] : string.Empty);
            }
            return value?.ToString() ?? string.Empty;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>List&lt;string&gt; → comma-separated string</summary>
    public class ListToStringConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is IEnumerable<string> list ? string.Join(", ", list) : string.Empty;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>AI message role → background brush</summary>
    public class RoleToBackgroundConverter : IValueConverter
    {
        private static readonly SolidColorBrush UserBg      = BrushHelper.Freeze(Color.FromRgb(30,  45,  60));
        private static readonly SolidColorBrush AssistantBg = BrushHelper.Freeze(Color.FromRgb(28,  28,  34));
        private static readonly SolidColorBrush DefaultBg   = BrushHelper.Freeze(Color.FromRgb(40,  40,  40));

        public object Convert(object value, Type t, object p, CultureInfo c) =>
            (value as string) switch
            {
                "user"      => UserBg,
                "assistant" => AssistantBg,
                _           => DefaultBg
            };
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>AI message role → foreground brush</summary>
    public class RoleToForegroundConverter : IValueConverter
    {
        private static readonly SolidColorBrush UserFg      = BrushHelper.Freeze(Color.FromRgb(232, 135,  34));
        private static readonly SolidColorBrush AssistantFg = BrushHelper.Freeze(Color.FromRgb(0,   183, 120));

        public object Convert(object value, Type t, object p, CultureInfo c) =>
            (value as string) switch
            {
                "user"      => UserFg,
                "assistant" => AssistantFg,
                _           => (object)Brushes.Gray
            };
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>AI message role → display label</summary>
    public class RoleToLabelConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) =>
            (value as string) switch
            {
                "user"      => "You",
                "assistant" => "AI Assistant",
                "system"    => "System",
                _           => value?.ToString() ?? string.Empty
            };
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>WikiSectionType → colour for section type badge</summary>
    public class SectionTypeToColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush CommandColor = BrushHelper.Freeze(Color.FromRgb(232, 119, 34));
        private static readonly SolidColorBrush TextColor    = BrushHelper.Freeze(Color.FromRgb( 60, 120, 60));

        public object Convert(object value, Type t, object p, CultureInfo c) =>
            value is WikiSectionType type
                ? type == WikiSectionType.Command ? CommandColor : TextColor
                : (object)Brushes.Gray;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    /// <summary>int == 0 → Visible (empty-list placeholder), int > 0 → Collapsed</summary>
    public class ZeroCountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }
}
