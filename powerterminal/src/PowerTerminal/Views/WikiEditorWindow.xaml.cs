using System.Windows;
using PowerTerminal.ViewModels;

namespace PowerTerminal.Views
{
    public partial class WikiEditorWindow : Window
    {
        public WikiEditorWindow(WikiEditorViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            vm.SaveRequested   += () => { DialogResult = true;  Close(); };
            vm.CancelRequested += () => { DialogResult = false; Close(); };
        }
    }
}
