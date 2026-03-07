using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Input;

namespace PowerTerminal.Views
{
    public partial class SshPasswordPromptWindow : Window
    {
        /// <summary>
        /// Returns the entered password as a plain string, converting from SecureString
        /// only at the point of use to minimise time in managed memory.
        /// </summary>
        public string Password
        {
            get
            {
                var secure = PasswordInput.SecurePassword;
                if (secure.Length == 0) return string.Empty;
                var ptr = Marshal.SecureStringToBSTR(secure);
                try
                {
                    return Marshal.PtrToStringBSTR(ptr) ?? string.Empty;
                }
                finally
                {
                    Marshal.ZeroFreeBSTR(ptr);
                }
            }
        }

        public SshPasswordPromptWindow(string username, string prompt)
        {
            InitializeComponent();
            PromptLabel.Text = string.IsNullOrWhiteSpace(prompt)
                ? $"Password for {username}:"
                : $"{prompt} ({username})";
            Loaded += (_, _) => PasswordInput.Focus();
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
