using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PowerTerminal.Views
{
    public partial class DarkChoiceWindow : Window
    {
        public string? SelectedChoice { get; private set; }

        public DarkChoiceWindow(string title, string message, string[] choices)
        {
            InitializeComponent();
            TitleText.Text   = title;
            MessageText.Text = message;

            bool first = true;
            foreach (string choice in choices)
            {
                string captured = choice;
                var btn = new Button
                {
                    Content  = captured,
                    MinWidth = 80,
                    Margin   = new Thickness(first ? 0 : 8, 0, 0, 0)
                };
                btn.Style = first
                    ? (Style)FindResource("AccentButton")
                    : (Style)FindResource("FlatButton");
                btn.Click += (_, _) =>
                {
                    SelectedChoice = captured;
                    DialogResult = true;
                };
                ButtonPanel.Children.Add(btn);
                first = false;
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            SelectedChoice = null;
            DialogResult   = false;
        }
    }
}
