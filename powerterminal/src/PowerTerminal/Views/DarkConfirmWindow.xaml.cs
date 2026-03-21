using System.Windows;
using System.Windows.Input;

namespace PowerTerminal.Views
{
    public partial class DarkConfirmWindow : Window
    {
        public DarkConfirmWindow(string title, string message)
        {
            InitializeComponent();
            TitleText.Text   = title;
            MessageText.Text = message;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Yes_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void No_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
