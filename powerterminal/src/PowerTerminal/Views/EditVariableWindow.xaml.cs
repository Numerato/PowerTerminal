using System;
using System.Windows;
using System.Windows.Input;

namespace PowerTerminal.Views
{
    public partial class EditVariableWindow : Window
    {
        private readonly Func<string, bool>? _isDuplicateCheck;

        public string VariableName { get; private set; }
        public string VariableValue { get; private set; }

        public EditVariableWindow(string name, string value, Func<string, bool>? isDuplicateCheck = null)
        {
            InitializeComponent();
            NameTextBox.Text = name;
            ValueTextBox.Text = value;
            VariableName = name;
            VariableValue = value;
            _isDuplicateCheck = isDuplicateCheck;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string name = NameTextBox.Text.Trim();
            if (!name.StartsWith("$")) name = "$" + name;
            if (!name.EndsWith("$")) name = name + "$";

            // Check for duplicate
            if (_isDuplicateCheck != null && _isDuplicateCheck(name))
            {
                DarkMessageBox.Show(
                    this,
                    $"A variable named '{name}' already exists.",
                    "Duplicate Variable");
                return;
            }

            VariableName = name;
            VariableValue = ValueTextBox.Text.Trim();
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
