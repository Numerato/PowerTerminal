using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PowerTerminal.Models;
using PowerTerminal.ViewModels;

namespace PowerTerminal.Views
{
    public partial class WikiView : UserControl
    {
        public WikiView()
        {
            InitializeComponent();
        }

        private WikiViewModel? Vm => DataContext as WikiViewModel;

        private MainViewModel? MainVm =>
            (Window.GetWindow(this) as MainWindow)?.Vm;

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Vm?.SearchCommand.Execute(null);
        }

        private void NewWiki_Click(object sender, RoutedEventArgs e)
            => MainVm?.OpenWikiEditorCommand.Execute(null);

        private void EditWiki_Click(object sender, RoutedEventArgs e)
            => MainVm?.EditWikiCommand.Execute(null);

        private void DeleteWiki_Click(object sender, RoutedEventArgs e)
        {
            if (Vm?.SelectedEntry == null) return;
            var result = DarkMessageBox.Show(
                Window.GetWindow(this),
                $"Delete wiki '{Vm.SelectedEntry.Title}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                MainVm?.DeleteWikiCommand.Execute(null);
        }

        private void CopyCommand_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is WikiSection section && Vm?.SelectedEntry != null)
                Vm.CopyCommand(Vm.SelectedEntry, section.Content);
        }

        private void ExecuteCommand_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is WikiSection section && Vm?.SelectedEntry != null)
                Vm.ExecuteCommand(Vm.SelectedEntry, section.Content);
        }

        private void SectionControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ContentControl cc && cc.Content is WikiSection section)
            {
                string key = section.Type == WikiSectionType.Command
                    ? "CommandSectionTemplate"
                    : "TextSectionTemplate";
                if (TryFindResource(key) is DataTemplate tpl)
                    cc.ContentTemplate = tpl;
            }
        }
    }
}
