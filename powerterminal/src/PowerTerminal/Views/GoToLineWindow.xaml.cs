using System.Windows;
using System.Windows.Input;

namespace PowerTerminal.Views
{
    public partial class GoToLineWindow : Window
    {
        public int LineNumber { get; private set; }

        public GoToLineWindow(int currentLine)
        {
            InitializeComponent();
            LineInput.Text = currentLine.ToString();
            Loaded += (_, _) => { LineInput.Focus(); LineInput.SelectAll(); };
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(LineInput.Text.Trim(), out int n) && n > 0)
            {
                LineNumber   = n;
                DialogResult = true;
                Close();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void LineInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OK_Click(null, null);
        }
    }
}
