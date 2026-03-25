using System.Windows;
using System.Windows.Input;

namespace PowerTerminal.Views
{
    public partial class DarkPasswordWindow : Window
    {
        public DarkPasswordWindow(string title, string message)
        {
            InitializeComponent();
            TitleText.Text   = title;
            MessageText.Text = message;
        }

        public string Password => PasswordBox.Password;

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                DialogResult = true;
                Close();
            }
        }
    }
}
