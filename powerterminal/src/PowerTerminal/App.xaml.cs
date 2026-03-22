using System;
using System.IO;
using System.Windows;
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
        }
    }
}
