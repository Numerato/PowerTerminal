using System;
using System.Windows;
using System.Windows.Media;
using PowerTerminal.Services;

namespace PowerTerminal.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly ConfigService _config = new();

        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            var s = _config.LoadSettings();
            ApiBaseUrl.Text    = s.Ai.ApiBaseUrl;
            ApiToken.Password  = s.Ai.ApiToken;
            ModelName.Text     = s.Ai.Model;
            Temperature.Value  = s.Ai.Temperature;
            SystemPrompt.Text  = s.Ai.SystemPrompt;
            FontFamilyInput.Text    = s.Theme.FontFamily;
            FontSizeInput.Text      = s.Theme.FontSize.ToString();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var s = _config.LoadSettings();
            s.Ai.ApiBaseUrl   = ApiBaseUrl.Text.Trim();
            s.Ai.ApiToken     = ApiToken.Password;
            s.Ai.Model        = ModelName.Text.Trim();
            s.Ai.Temperature  = Temperature.Value;
            s.Ai.SystemPrompt = SystemPrompt.Text;
            s.Theme.FontFamily = FontFamilyInput.Text.Trim();
            if (double.TryParse(FontSizeInput.Text, out double fs)) s.Theme.FontSize = fs;
            _config.SaveSettings(s);
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
