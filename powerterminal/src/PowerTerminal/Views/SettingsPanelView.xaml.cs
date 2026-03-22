using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PowerTerminal.Services;

namespace PowerTerminal.Views
{
    public partial class SettingsPanelView : UserControl
    {
        private readonly ConfigService _config = new();

        public SettingsPanelView()
        {
            InitializeComponent();
            Loaded += (_, _) => LoadSettings();
        }

        private void LoadSettings()
        {
            var s = _config.LoadSettings();
            DebugLoggingCheck.IsChecked  = s.EnableDebugLogging;
            EnablePowerEditCheck.IsChecked = s.EnablePowerEdit;
            ApiBaseUrl.Text              = s.Ai.ApiBaseUrl;
            ApiToken.Password            = s.Ai.ApiToken;
            ModelName.Text               = s.Ai.Model;
            Temperature.Value            = s.Ai.Temperature;
            SystemPrompt.Text            = s.Ai.SystemPrompt;
            FontFamilyInput.Text         = s.Theme.FontFamily;
            FontSizeInput.Text           = s.Theme.FontSize.ToString(CultureInfo.InvariantCulture);
            SshKeysFolderInput.Text      = s.SshKeysFolder;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var s = _config.LoadSettings();
            s.EnableDebugLogging  = DebugLoggingCheck.IsChecked == true;
            s.EnablePowerEdit     = EnablePowerEditCheck.IsChecked == true;
            s.Ai.ApiBaseUrl       = ApiBaseUrl.Text.Trim();
            s.Ai.ApiToken         = ApiToken.Password;
            s.Ai.Model            = ModelName.Text.Trim();
            s.Ai.Temperature      = Temperature.Value;
            s.Ai.SystemPrompt     = SystemPrompt.Text;
            s.Theme.FontFamily    = FontFamilyInput.Text.Trim();
            if (double.TryParse(FontSizeInput.Text, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double fs))
                s.Theme.FontSize = fs;
            s.SshKeysFolder       = SshKeysFolderInput.Text.Trim();
            _config.SaveSettings(s);

            MessageBox.Show("Settings saved. Reconnect active tabs for theme changes to take effect.",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
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

