using System;
using System.Windows;
using System.Windows.Controls;
using PowerTerminal.Models;
using PowerTerminal.ViewModels;
using PowerTerminal.Views;

namespace PowerTerminal
{
    public partial class MainWindow : Window
    {
        internal MainViewModel Vm { get; } = new MainViewModel();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = Vm;

            Vm.OpenConnectionManagerRequested += OpenConnectionManager;
            Vm.OpenSettingsRequested          += OpenSettings;
            Vm.OpenWikiEditorRequested        += OpenWikiEditor;
            Vm.VariablePromptRequested        += PromptVariable;

            StateChanged += (_, _) => UpdateMaxRestoreIcon();
        }

        // ── Tab lifecycle ────────────────────────────────────────────────────

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TerminalTabViewModel tab)
                Vm.RemoveTab(tab);
        }

        // ── Connect dropdown ─────────────────────────────────────────────────

        private void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            ConnectPopup.IsOpen = true;
        }

        private void ConnectItem_Click(object sender, RoutedEventArgs e)
        {
            ConnectPopup.IsOpen = false;
            if (sender is Button btn && btn.Tag is SshConnection conn)
                Vm.ConnectToConnection(conn);
        }

        private void ManageConnections_Click(object sender, RoutedEventArgs e)
        {
            ConnectPopup.IsOpen = false;
            OpenConnectionManager();
        }

        // ── Title bar buttons ────────────────────────────────────────────────

        private void Settings_Click(object sender, RoutedEventArgs e)
            => Vm.OpenSettingsCommand.Execute(null);

        private void Minimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
            => Close();

        private void UpdateMaxRestoreIcon()
        {
            bool maximized = WindowState == WindowState.Maximized;
            MaxRestoreIcon.Text      = maximized ? "\uE923" : "\uE922";
            MaxRestoreBtn.ToolTip    = maximized ? "Restore" : "Maximize";
        }

        // ── Dialog helpers ────────────────────────────────────────────────────

        private void OpenConnectionManager()
        {
            var win = new ConnectionManagerWindow(Vm.ConnectionManager) { Owner = this };
            win.ShowDialog();
            if (win.DialogResult == true && Vm.ConnectionManager.Selected != null)
                Vm.ConnectSelectedCommand.Execute(null);
        }

        private void OpenSettings()
        {
            var win = new SettingsWindow { Owner = this };
            win.ShowDialog();
        }

        private void OpenWikiEditor(WikiEditorViewModel vm)
        {
            var win = new WikiEditorWindow(vm) { Owner = this };
            win.ShowDialog();
        }

        private string? PromptVariable(string name)
        {
            var dlg = new VariablePromptWindow(name) { Owner = this };
            return dlg.ShowDialog() == true ? dlg.Value : null;
        }
    }
}
