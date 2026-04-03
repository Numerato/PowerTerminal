using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PowerTerminal.Models;
using PowerTerminal.ViewModels;

namespace PowerTerminal.Views
{
    public partial class VariablesView : UserControl
    {
        public VariablesView()
        {
            InitializeComponent();
        }

        private void VariableMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Placement = PlacementMode.Bottom;
                button.ContextMenu.IsOpen = true;
            }
        }

        private void EditVariable_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is CustomVariable variable)
            {
                var vm = DataContext as VariablesViewModel;
                if (vm == null) return;

                var editWindow = new EditVariableWindow(
                    variable.Name,
                    variable.Value,
                    name => vm.IsDuplicateName(name, variable))
                {
                    Owner = Window.GetWindow(this)
                };

                if (editWindow.ShowDialog() == true)
                {
                    variable.Name = editWindow.VariableName;
                    variable.Value = editWindow.VariableValue;
                    vm.SaveCustomVariables();
                    vm.ResortCustomVariables();
                }
            }
        }

        private void DeleteVariable_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is CustomVariable variable)
            {
                var confirmDialog = new DarkConfirmWindow(
                    "Delete Variable",
                    $"Are you sure you want to delete '{variable.Name}'?",
                    defaultNo: true)
                {
                    Owner = Window.GetWindow(this)
                };

                if (confirmDialog.ShowDialog() == true)
                {
                    var vm = DataContext as VariablesViewModel;
                    vm?.DeleteCommand.Execute(variable);
                }
            }
        }
    }
}

