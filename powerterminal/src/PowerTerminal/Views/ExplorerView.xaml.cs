using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PowerTerminal.Models;
using PowerTerminal.ViewModels;

namespace PowerTerminal.Views
{
    public partial class ExplorerView : UserControl
    {
        private string _typeAheadBuffer = string.Empty;
        private DateTime _lastKeyTime = DateTime.MinValue;

        public ExplorerView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private RemoteExplorerViewModel? Vm => DataContext as RemoteExplorerViewModel;

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is RemoteExplorerViewModel vm)
                vm.PasswordPromptCallback = ShowPasswordDialog;
        }

        private string ShowPasswordDialog(string message)
        {
            var dlg = new DarkPasswordWindow("Sudo Authentication", message)
            {
                Owner = Window.GetWindow(this)
            };
            return dlg.ShowDialog() == true ? dlg.Password : string.Empty;
        }

        private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileList.SelectedItem is RemoteFileItem item)
                Vm?.OpenItem(item);
        }

        private void PathBar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Vm != null)
                _ = Vm.NavigateToAsync(PathBar.Text);
        }

        private void FileList_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (Vm == null || Vm.Items.Count == 0) return;

            if ((DateTime.UtcNow - _lastKeyTime).TotalSeconds > 1.0)
                _typeAheadBuffer = string.Empty;

            _lastKeyTime = DateTime.UtcNow;
            _typeAheadBuffer += e.Text.ToLowerInvariant();

            var match = Vm.Items.FirstOrDefault(i =>
                i.Name.StartsWith(_typeAheadBuffer, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                FileList.SelectedItem = match;
                FileList.ScrollIntoView(match);
            }
            e.Handled = true;
        }

        private void FileList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Back)
            {
                _typeAheadBuffer = string.Empty;
                _lastKeyTime = DateTime.MinValue;
            }
            else if (e.Key == Key.Enter)
            {
                if (FileList.SelectedItem is RemoteFileItem item)
                {
                    Vm?.OpenItem(item);
                    e.Handled = true;
                }
            }
        }
        private void SudoCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (Vm == null) return;
            // Prompt for password before actually enabling sudo
            string pass = ShowPasswordDialog("Enter sudo password to view all files:");
            if (!string.IsNullOrEmpty(pass))
            {
                _ = Vm.EnableSudoWithPasswordAsync(pass);
            }
            else
            {
                // User cancelled — uncheck the box without triggering Unchecked event
                SudoCheckBox.IsChecked = false;
            }
        }

        private void SudoCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (Vm == null) return;
            _ = Vm.DisableSudoAsync();
        }
    }
}


