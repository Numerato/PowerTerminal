using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PowerTerminal.Models;
using PowerTerminal.ViewModels;

namespace PowerTerminal.Views
{
    public partial class ConnectionManagerWindow : Window
    {
        public ConnectionManagerWindow(ConnectionManagerViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            SyncIconComboBox();
            // Re-sync the icon ComboBox whenever a different connection is loaded into the editor
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ConnectionManagerViewModel.Editing))
                    SyncIconComboBox();
            };
        }

        private ConnectionManagerViewModel Vm => (ConnectionManagerViewModel)DataContext;

        // ── Dark chrome ──────────────────────────────────────────────────────

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseDialog_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ── Icon picker ──────────────────────────────────────────────────────

        /// <summary>
        /// Sync the ComboBox selection to match the current Editing.LogoPath.
        /// Called when the dialog opens and when Editing changes.
        /// </summary>
        private void SyncIconComboBox()
        {
            if (Vm?.Editing == null) return;
            var logoPath = Vm.Editing.LogoPath ?? string.Empty;
            // Match by relative path or by filename (handles legacy full paths stored in config)
            var match = Vm.IconOptions.FirstOrDefault(o =>
                string.Equals(o.Path, logoPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(o.Path), Path.GetFileName(logoPath), StringComparison.OrdinalIgnoreCase));
            IconComboBox.SelectedItem = match;
        }

        private void IconComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (IconComboBox.SelectedItem is IconOption option && Vm.Editing != null)
                Vm.Editing.LogoPath = option.Path;
        }

        private void BrowseLogo_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Select tab logo image",
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true && Vm.Editing != null)
            {
                Vm.Editing.LogoPath = dlg.FileName;
                // If the file is in the ico folder, try to select it in the combo
                SyncIconComboBox();
            }
        }

        // ── Save with confirmation ───────────────────────────────────────────

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.Editing == null) return;
            var confirm = new DarkConfirmWindow(
                "Save Connection",
                $"Save changes to \"{Vm.Editing.Name}\"?")
            {
                Owner = this
            };
            if (confirm.ShowDialog() == true)
                Vm.SaveCommand.Execute(null);
        }

        // ── Connect / close ──────────────────────────────────────────────────

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
