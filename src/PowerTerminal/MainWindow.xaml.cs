using System;
using System.Windows;
using System.Windows.Controls;
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
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TerminalTabViewModel tab)
            {
                tab.Disconnect();
                tab.Dispose();
                Vm.TerminalTabs.Remove(tab);
                if (Vm.TerminalTabs.Count == 0)
                    Vm.AddNewTab();
            }
        }

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
