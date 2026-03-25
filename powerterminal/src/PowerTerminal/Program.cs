using System.Runtime.InteropServices;
using System.Windows;

namespace PowerTerminal
{
    /// <summary>
    /// Custom entry point so we can stamp the explicit AUMID before WPF
    /// initialises any windows.  SetCurrentProcessExplicitAppUserModelID MUST
    /// be called before any UI work; doing it in App.OnStartup is too late
    /// because WPF may have already interacted with the shell by then.
    /// </summary>
    internal static class Program
    {
        private const string Aumid = "PowerTerminal.App";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        [System.STAThread]
        public static void Main(string[] args)
        {
            SetCurrentProcessExplicitAppUserModelID(Aumid);

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
