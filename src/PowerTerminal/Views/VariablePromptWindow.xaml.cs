using System.Windows;
using System.Windows.Input;

namespace PowerTerminal.Views
{
    public partial class VariablePromptWindow : Window
    {
        public string Value => ValueInput.Text;

        public VariablePromptWindow(string variableName)
        {
            InitializeComponent();
            PromptLabel.Text = $"Enter value for: {variableName}";
            Loaded += (_, _) => ValueInput.Focus();
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

        private void ValueInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                DialogResult = true;
                Close();
            }
        }
    }
}
