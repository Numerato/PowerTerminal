using System.Windows;

namespace PowerTerminal.Views
{
    /// <summary>
    /// Drop-in dark-themed replacement for <see cref="MessageBox"/>.
    /// All pop-ups in PowerTerminal should go through this class.
    /// </summary>
    public static class DarkMessageBox
    {
        /// <summary>Show a dark message dialog. Pass <paramref name="owner"/> where possible.</summary>
        public static MessageBoxResult Show(
            Window owner,
            string message,
            string title                = "Information",
            MessageBoxButton buttons    = MessageBoxButton.OK,
            MessageBoxImage icon        = MessageBoxImage.None,
            MessageBoxResult defaultResult = MessageBoxResult.None)
        {
            var win = new DarkMessageBoxWindow(title, message, buttons, icon, defaultResult);
            if (owner != null)
                win.Owner = owner;
            else
                win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            win.ShowDialog();
            return win.Result;
        }

        /// <summary>Ownerless overload — centres on screen (e.g. App-level crash handler).</summary>
        public static MessageBoxResult Show(
            string message,
            string title                = "Information",
            MessageBoxButton buttons    = MessageBoxButton.OK,
            MessageBoxImage icon        = MessageBoxImage.None,
            MessageBoxResult defaultResult = MessageBoxResult.None)
            => Show(null, message, title, buttons, icon, defaultResult);
    }
}
