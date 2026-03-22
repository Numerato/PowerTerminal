using System.Windows;
using System.Windows.Input;

namespace PowerTerminal.Views
{
    /// <summary>
    /// Shown when a file is readable but not writable.
    /// Result: null = cancelled, false = read-only, true = edit with sudo.
    /// </summary>
    public partial class PowerEditOpenModeWindow : Window
    {
        public PowerEditOpenModeWindow(string filename)
        {
            InitializeComponent();
            MessageText.Text = $"\"{System.IO.Path.GetFileName(filename)}\"";
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void ReadOnly_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false; // false = open read-only
            Close();
        }

        private void EditSudo_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true; // true = edit with sudo
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // DialogResult stays null = cancelled
            Close();
        }
    }
}
