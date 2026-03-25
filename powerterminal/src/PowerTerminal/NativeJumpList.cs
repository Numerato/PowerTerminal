using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using PowerTerminal.Models;

namespace PowerTerminal
{
    /// <summary>
    /// Builds the Windows taskbar jump list directly via the Shell COM API
    /// (ICustomDestinationList).  The WPF JumpList wrapper is unreliable on
    /// .NET 10 — it can silently discard every item when the icon path is
    /// invalid or when AUMID matching fails.  Calling the COM API directly
    /// gives us full error visibility and is the same approach used by every
    /// well-known terminal/IDE that supports jump lists.
    /// </summary>
    internal static class NativeJumpList
    {
        // ── CLSIDs ────────────────────────────────────────────────────────────

        private static readonly Guid CLSID_DestinationList =
            new("77F10CF0-3DB5-4966-B520-B7C54FD35ED6");

        private static readonly Guid CLSID_EnumerableObjectCollection =
            new("2D3468C1-36A7-43B6-AC24-D3F02FD9607A");

        private static readonly Guid CLSID_ShellLink =
            new("00021401-0000-0000-C000-000000000046");

        // IID used only to receive the rejected-destinations array from BeginList.
        private static readonly Guid IID_IObjectArray =
            new("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9");

        // ── Property keys ─────────────────────────────────────────────────────

        // PKEY_Title  {F29F85E0-4FF9-1068-AB91-08002B27B3D9} pid=2
        private static PROPERTYKEY PKEY_Title = new()
        {
            fmtid = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"),
            pid   = 2
        };

        private const string Aumid = "PowerTerminal.App";

        // ── Public entry point ────────────────────────────────────────────────

        /// <summary>
        /// Commits a "Connections" custom category to the Windows jump list.
        /// Returns <c>null</c> on success or the caught exception on failure.
        /// </summary>
        public static Exception? Rebuild(
            IEnumerable<SshConnection> connections,
            string exePath,
            string exeDir,
            string? defaultIconPath)
        {
            ICustomDestinationList? destList   = null;
            IObjectCollection?      collection = null;
            try
            {
                destList = (ICustomDestinationList)Activator.CreateInstance(
                    Type.GetTypeFromCLSID(CLSID_DestinationList, throwOnError: true)!)!;

                destList.SetAppID(Aumid);

                var iid = IID_IObjectArray;
                destList.BeginList(out _, ref iid, out _);

                collection = (IObjectCollection)Activator.CreateInstance(
                    Type.GetTypeFromCLSID(CLSID_EnumerableObjectCollection, throwOnError: true)!)!;

                int added = 0;
                foreach (var conn in connections)
                {
                    object? link = CreateShellLink(conn, exePath, exeDir, defaultIconPath);
                    if (link is null) continue;
                    try
                    {
                        collection.Add(link);
                        added++;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(link);
                    }
                }

                if (added > 0)
                    destList.AppendCategory("Connections", collection);

                destList.CommitList();
                return null;
            }
            catch (Exception ex)
            {
                try { destList?.AbortList(); } catch { /* best-effort */ }
                return ex;
            }
            finally
            {
                if (collection is not null) Marshal.ReleaseComObject(collection);
                if (destList   is not null) Marshal.ReleaseComObject(destList);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static object? CreateShellLink(
            SshConnection conn,
            string exePath,
            string exeDir,
            string? defaultIconPath)
        {
            try
            {
                var link = (IShellLinkW)Activator.CreateInstance(
                    Type.GetTypeFromCLSID(CLSID_ShellLink, throwOnError: true)!)!;

                link.SetPath(exePath);
                link.SetArguments($"--connect {conn.Id}");
                link.SetDescription($"{conn.Username}@{conn.Host}:{conn.Port}");
                link.SetWorkingDirectory(exeDir);

                string? iconPath = ResolveIcon(conn.LogoPath, exeDir)
                                ?? (defaultIconPath is not null && File.Exists(defaultIconPath)
                                        ? defaultIconPath : null);
                if (iconPath is not null)
                    link.SetIconLocation(iconPath, 0);

                // Set the visible title via IPropertyStore (required for jump-list display).
                var store = (IPropertyStore)link;
                var title = PropVariant.FromString(conn.Name);
                try   { store.SetValue(ref PKEY_Title, ref title); store.Commit(); }
                finally { title.Dispose(); }

                return link;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Resolves a connection's logo path to an existing .ico file.
        /// Returns <c>null</c> when no suitable icon can be found.
        /// </summary>
        internal static string? ResolveIcon(string? logoPath, string baseDir)
        {
            if (string.IsNullOrEmpty(logoPath)) return null;

            string full = Path.IsPathRooted(logoPath)
                ? logoPath
                : Path.Combine(baseDir, logoPath);

            if (string.Equals(Path.GetExtension(full), ".ico",
                    StringComparison.OrdinalIgnoreCase))
                return File.Exists(full) ? full : null;

            // Try swapping .png / .jpg → .ico (e.g. ico\linux.png → ico\linux.ico).
            string icoPath = Path.ChangeExtension(full, ".ico");
            return File.Exists(icoPath) ? icoPath : null;
        }

        // ── COM interfaces ────────────────────────────────────────────────────

        [ComImport]
        [Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICustomDestinationList
        {
            void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
            void BeginList(out uint pcMinSlots, ref Guid riid,
                           [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
            void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory,
                                [MarshalAs(UnmanagedType.IUnknown)] object poa);
            void AppendKnownCategory(int category);
            void AddUserTasks([MarshalAs(UnmanagedType.IUnknown)] object poa);
            void CommitList();
            void GetRemovedDestinations(ref Guid riid,
                                        [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
            void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
            void AbortList();
        }

        // IObjectCollection inherits IObjectArray; redeclare all methods in vtable order.
        [ComImport]
        [Guid("5632B1A4-E38A-400A-928A-D4CD63230295")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IObjectCollection
        {
            // ── IObjectArray methods (vtable slots 3-4) ──
            void GetCount(out uint pcObjects);
            void GetAt(uint uiIndex, ref Guid riid,
                       [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            // ── IObjectCollection-specific methods (vtable slots 5-8) ──
            void Add([MarshalAs(UnmanagedType.IUnknown)] object pvObject);
            void AddFromArray([MarshalAs(UnmanagedType.IUnknown)] object poaSource);
            void RemoveObjectAt(uint uiIndex);
            void Clear();
        }

        [ComImport]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
                         int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out ushort pwHotkey);
            void SetHotkey(ushort wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
                                 int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            void GetCount(out uint cProps);
            void GetAt(uint iProp, out PROPERTYKEY pkey);
            void GetValue(ref PROPERTYKEY key, out PropVariant pv);
            void SetValue(ref PROPERTYKEY key, ref PropVariant pv);
            void Commit();
        }

        // ── Value types ───────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        /// <summary>
        /// Minimal PROPVARIANT capable of carrying a VT_LPWSTR (0x001F) string.
        /// Size is fixed at 16 bytes to match the native structure on all platforms.
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 16)]
        private struct PropVariant : IDisposable
        {
            [FieldOffset(0)] public ushort vt;      // VARTYPE
            [FieldOffset(8)] public IntPtr pszVal;  // union — LPWSTR for VT_LPWSTR

            public static PropVariant FromString(string value) =>
                new() { vt = 0x001F, pszVal = Marshal.StringToCoTaskMemUni(value) };

            public void Dispose()
            {
                if (vt == 0x001F && pszVal != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(pszVal);
            }
        }
    }
}
