using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PowerTerminal.Models;
using PowerTerminal.Services;
using PowerTerminal.Views;

namespace PowerTerminal
{
    public partial class App : Application
    {
        private const string Aumid = "PowerTerminal.App";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            RegisterAumid();

            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    Directory.CreateDirectory(logDir);
                    string logPath = Path.Combine(logDir,
                        $"crash_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
                    File.WriteAllText(logPath,
                        $"[{DateTime.Now:o}] Unhandled exception\r\n{args.Exception}\r\n");
                }
                catch { }

                DarkMessageBox.Show(
                    $"Unhandled error: {args.Exception.Message}\n\n{args.Exception.StackTrace}",
                    "PowerTerminal Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };

            RebuildJumpList();
        }

        /// <summary>
        /// Keeps the HKCU AppUserModelId registry entry up-to-date with the
        /// current exe path so Windows can resolve the AUMID back to our exe.
        /// </summary>
        private static void RegisterAumid()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule!.FileName;
                string exeDir  = Path.GetDirectoryName(exePath)!;
                string icoPath = Path.Combine(exeDir, "Images", "powerterminal.ico");

                const string regBase = @"Software\Classes\AppUserModelId\" + Aumid;
                using var key = Registry.CurrentUser.CreateSubKey(regBase, writable: true);
                key.SetValue("ApplicationName",        "PowerTerminal");
                key.SetValue("ApplicationDescription", "SSH Terminal");
                if (File.Exists(icoPath))
                    key.SetValue("ApplicationIcon", $"{icoPath},0");

                const string appPaths = @"Software\Microsoft\Windows\CurrentVersion\App Paths\PowerTerminal.exe";
                using var apKey = Registry.CurrentUser.CreateSubKey(appPaths, writable: true);
                apKey.SetValue("", exePath);
                apKey.SetValue("Path", exeDir);
            }
            catch { /* best-effort */ }
        }

        /// <summary>
        /// Rebuilds the Windows taskbar jump list. Safe to call at any time from the UI thread.
        /// </summary>
        public static void RebuildJumpList(IEnumerable<SshConnection>? connections = null)
        {
            try
            {
                connections ??= new ConfigService().LoadConnections();

                string exePath = Process.GetCurrentProcess().MainModule!.FileName;
                string exeDir  = Path.GetDirectoryName(exePath)!;

                string candidate    = Path.Combine(exeDir, "Images", "powerterminal.ico");
                string? defaultIcon = File.Exists(candidate) ? candidate : null;

                Exception? ex = NativeJumpList.Rebuild(connections, exePath, exeDir, defaultIcon);
                if (ex is not null)
                    LogJumpListError(ex);
            }
            catch (Exception ex)
            {
                LogJumpListError(ex);
            }
        }

        private static void LogJumpListError(Exception ex)
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PowerTerminal", "logs");
                Directory.CreateDirectory(logDir);
                File.AppendAllText(Path.Combine(logDir, "startup.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] JumpList error: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
