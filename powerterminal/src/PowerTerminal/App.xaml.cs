using System.Windows;

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
                MessageBox.Show(
                    $"Unhandled error: {args.Exception.Message}\n\n{args.Exception.StackTrace}",
                    "PowerTerminal Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}
