using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
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

                string exePath = Process.GetCurrentProcess().MainModule!.FileName;
                string exeDir  = Path.GetDirectoryName(exePath)!;

                // Use the .ico that ships next to the exe.  If it is missing (e.g. a
                // stripped publish layout), pass null so individual items stay icon-less
                // rather than having the entire category silently rejected by Windows.
                string candidate   = Path.Combine(exeDir, "Images", "powerterminal.ico");
                string? defaultIcon = File.Exists(candidate) ? candidate : null;

                Exception? ex = NativeJumpList.Rebuild(connections, exePath, exeDir, defaultIcon);
                if (ex is not null)
                    LogJumpListError(ex);
            }
            catch (Exception ex)
            {
                // Catch anything that escaped NativeJumpList (e.g. ConfigService failure).
                LogJumpListError(ex);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static void LogJumpListError(Exception ex)
        {
            try
            {
                // Append to the same startup.log that ConfigService uses so the
                // error appears alongside the connection-count diagnostic.
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PowerTerminal", "logs");
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, "startup.log");
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] JumpList error: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
            }
            catch { /* logging must never crash the app */ }
        }
    }
}
