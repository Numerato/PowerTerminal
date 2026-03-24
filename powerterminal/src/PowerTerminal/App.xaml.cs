using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Shell;
using PowerTerminal.Models;
using PowerTerminal.Services;
using PowerTerminal.Views;

namespace PowerTerminal
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Global exception handler
            DispatcherUnhandledException += (s, args) =>
            {
                // Persist the crash details before the dialog so they are never lost.
                try
                {
                    string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    Directory.CreateDirectory(logDir);
                    string logPath = Path.Combine(logDir,
                        $"crash_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
                    File.WriteAllText(logPath,
                        $"[{DateTime.Now:o}] Unhandled exception\r\n{args.Exception}\r\n");
                }
                catch { /* never let a logging failure suppress the original error */ }

                DarkMessageBox.Show(
                    $"Unhandled error: {args.Exception.Message}\n\n{args.Exception.StackTrace}",
                    "PowerTerminal Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };

            // Build taskbar jump list so pinned connections appear in the right-click menu.
            RebuildJumpList();
        }

        /// <summary>
        /// Rebuilds the Windows taskbar jump list from the supplied connections (or reloads
        /// from disk when <paramref name="connections"/> is null).  Safe to call at any time
        /// from the UI thread.
        /// </summary>
        public static void RebuildJumpList(IEnumerable<SshConnection>? connections = null)
        {
            try
            {
                connections ??= new ConfigService().LoadConnections();

                var jumpList = new JumpList();
                jumpList.ShowFrequentCategory = false;
                jumpList.ShowRecentCategory   = false;

                string exePath   = Process.GetCurrentProcess().MainModule!.FileName;
                string exeDir    = Path.GetDirectoryName(exePath)!;
                string defaultIcon = Path.Combine(exeDir, "Images", "powerterminal.ico");

                foreach (var conn in connections)
                {
                    jumpList.JumpItems.Add(new JumpTask
                    {
                        Title              = conn.Name,
                        Description        = $"{conn.Username}@{conn.Host}:{conn.Port}",
                        ApplicationPath    = exePath,
                        // Explicitly set the working directory so Windows does not default
                        // it to C:\Windows\system32 (which breaks all relative-path resolution).
                        WorkingDirectory   = exeDir,
                        Arguments          = $"--connect {conn.Id}",
                        IconResourcePath   = ResolveJumpListIcon(conn.LogoPath, exeDir) ?? defaultIcon,
                        IconResourceIndex  = 0,
                        CustomCategory     = "Connections"
                    });
                }

                JumpList.SetJumpList(Current, jumpList);
                jumpList.Apply();
            }
            catch
            {
                // Never let jump-list errors crash the application.
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves a connection's LogoPath to an .ico file path suitable for a JumpTask icon.
        /// Returns null when no suitable .ico is found (caller falls back to the app icon).
        /// </summary>
        private static string? ResolveJumpListIcon(string? logoPath, string baseDir)
        {
            if (string.IsNullOrEmpty(logoPath)) return null;

            string full = Path.IsPathRooted(logoPath)
                ? logoPath
                : Path.Combine(baseDir, logoPath);

            // Already an .ico? Use it directly.
            if (string.Equals(Path.GetExtension(full), ".ico", StringComparison.OrdinalIgnoreCase))
                return File.Exists(full) ? full : null;

            // Try swapping .png / .jpg → .ico (e.g. ico\linux.png → ico\linux.ico).
            string icoPath = Path.ChangeExtension(full, ".ico");
            return File.Exists(icoPath) ? icoPath : null;
        }
    }
}
