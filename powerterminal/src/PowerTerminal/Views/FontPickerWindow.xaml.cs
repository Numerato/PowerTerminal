using System.Windows;
using System.Windows.Input;

namespace PowerTerminal.Views
{
    public partial class FontPickerWindow : Window
    {
        public string SelectedFamily { get; private set; }
        public double SelectedSize   { get; private set; }

        private static readonly string[] CommonFonts = {
            "Consolas", "Courier New", "Lucida Console", "Cascadia Code",
            "Cascadia Mono", "Fira Code", "JetBrains Mono", "Source Code Pro",
            "Segoe UI", "Arial", "Calibri"
        };

        public FontPickerWindow(string currentFamily, double currentSize)
        {
            InitializeComponent();
            FamilyCombo.ItemsSource  = CommonFonts;
            FamilyCombo.Text         = currentFamily;
            SizeInput.Text           = currentSize.ToString();
            SelectedFamily           = currentFamily;
            SelectedSize             = currentSize;
            Loaded += (_, _) => FamilyCombo.Focus();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            string family = (FamilyCombo.Text ?? "Consolas").Trim();
            if (string.IsNullOrEmpty(family)) family = "Consolas";

            if (!double.TryParse(SizeInput.Text.Trim(), out double size) || size < 6 || size > 72)
                size = 13;

            SelectedFamily = family;
            SelectedSize   = size;
            DialogResult   = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SizeInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OK_Click(null, null);
        }
    }
}
