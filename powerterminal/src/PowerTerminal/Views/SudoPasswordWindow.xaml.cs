using System.Windows;
using System.Windows.Input;

namespace PowerTerminal.Views
{
    public partial class SudoPasswordWindow : Window
    {
        public string Password => PasswordInput.Password;

        public SudoPasswordWindow(string message = "")
        {
            InitializeComponent();
            if (!string.IsNullOrEmpty(message))
                MessageText.Text = message;
            Loaded += (_, _) => PasswordInput.Focus();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                DialogResult = true;
                Close();
            }
        }
    }
}
