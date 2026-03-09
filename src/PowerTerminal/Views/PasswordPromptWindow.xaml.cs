using System.Windows;
using System.Windows.Input;

namespace PowerTerminal.Views
{
    public partial class PasswordPromptWindow : Window
    {
        public string Password { get; private set; } = string.Empty;

        public PasswordPromptWindow(string promptText)
        {
            InitializeComponent();
            PromptText.Text = promptText;
            Loaded += (_, _) => PasswordInput.Focus();
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            Password     = PasswordInput.Password;
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
                Password     = PasswordInput.Password;
                DialogResult = true;
                Close();
            }
        }
    }
}
