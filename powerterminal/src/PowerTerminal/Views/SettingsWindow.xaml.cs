using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PowerTerminal.Services;
using System.Globalization;

namespace PowerTerminal.Views
{
    public partial class SettingsWindow
    {
        private readonly ConfigService _config = new();

        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void LoadSettings()
        {
            var s = _config.LoadSettings();
            DebugLoggingCheck.IsChecked = s.EnableDebugLogging;
            ApiBaseUrl.Text         = s.Ai.ApiBaseUrl;
            ApiToken.Password       = s.Ai.ApiToken;
            ModelName.Text          = s.Ai.Model;
            Temperature.Value       = s.Ai.Temperature;
            SystemPrompt.Text       = s.Ai.SystemPrompt;
            FontFamilyInput.Text    = s.Theme.FontFamily;
            FontSizeInput.Text      = s.Theme.FontSize.ToString(CultureInfo.InvariantCulture);
            SshKeysFolderInput.Text = s.SshKeysFolder;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var s = _config.LoadSettings();
            s.EnableDebugLogging = DebugLoggingCheck.IsChecked == true;
            s.Ai.ApiBaseUrl    = ApiBaseUrl.Text.Trim();
            s.Ai.ApiToken      = ApiToken.Password;
            s.Ai.Model         = ModelName.Text.Trim();
            s.Ai.Temperature   = Temperature.Value;
            s.Ai.SystemPrompt  = SystemPrompt.Text;
            s.Theme.FontFamily = FontFamilyInput.Text.Trim();
            if (double.TryParse(FontSizeInput.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double fs)) s.Theme.FontSize = fs;
            s.SshKeysFolder    = SshKeysFolderInput.Text.Trim();
            _config.SaveSettings(s);
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BrowseSshFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title           = "Select SSH Keys Folder",
                Filter          = "Folders|*.none",
                CheckFileExists = false,
                CheckPathExists = true,
                FileName        = "Select Folder",
                ValidateNames   = false
            };
            if (Directory.Exists(SshKeysFolderInput.Text))
                dlg.InitialDirectory = SshKeysFolderInput.Text;
            if (dlg.ShowDialog() == true)
            {
                var dir = Path.GetDirectoryName(dlg.FileName);
                if (!string.IsNullOrEmpty(dir))
                    SshKeysFolderInput.Text = dir;
            }
        }
    }
}
