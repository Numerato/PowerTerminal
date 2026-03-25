using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.IO;

namespace PowerTerminal.Services
{
    /// <summary>
    /// Extracts shell file-type icons from Windows using SHGetFileInfo.
    /// Results are cached by extension so each type is only fetched once.
    /// No real files are needed — SHGFI_USEFILEATTRIBUTES means Windows
    /// derives the icon purely from the extension/attributes.
    /// </summary>
    internal static class FileIconService
    {
        private static readonly Dictionary<string, BitmapSource> _cache = new(StringComparer.OrdinalIgnoreCase);

        public static BitmapSource GetIcon(string fileName, bool isDirectory)
        {
            string key = isDirectory
                ? "\x00DIR\x00"
                : Path.GetExtension(fileName).ToLowerInvariant();

            if (_cache.TryGetValue(key, out var cached)) return cached;

            var icon = ExtractShellIcon(fileName, isDirectory);
            _cache[key] = icon;
            return icon;
        }

        private static BitmapSource ExtractShellIcon(string fileName, bool isDirectory)
        {
            // Use a fake path — only the extension matters for non-directories.
            string fakePath = isDirectory ? "folder" : "file" + Path.GetExtension(fileName);

            var shfi = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
            uint attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

            IntPtr result = SHGetFileInfo(fakePath, attrs, ref shfi,
                (uint)Marshal.SizeOf<SHFILEINFO>(), flags);

            if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;

            try
            {
                var bs = Imaging.CreateBitmapSourceFromHIcon(
                    shfi.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                return bs;
            }
            catch
            {
                return null;
            }
            finally
            {
                DestroyIcon(shfi.hIcon);
            }
        }

        // ── P/Invokes ─────────────────────────────────────────────────────────

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbSizeFileInfo,
            uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint SHGFI_ICON              = 0x000000100;
        private const uint SHGFI_SMALLICON         = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint FILE_ATTRIBUTE_NORMAL    = 0x00000080;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int    iIcon;
            public uint   dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]  public string szTypeName;
        }
    }
}
