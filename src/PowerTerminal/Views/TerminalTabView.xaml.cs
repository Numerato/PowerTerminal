using System.Windows;
using System.Windows.Controls;
using PowerTerminal.ViewModels;

namespace PowerTerminal.Views
{
    public partial class TerminalTabView : UserControl
    {
        public TerminalTabView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
        }

        private TerminalTabViewModel? _vm;

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null)
            {
                _vm.TerminalDataReceived -= OnTerminalData;
                _vm.LocalOutput          -= OnTerminalData;
            }

            _vm = DataContext as TerminalTabViewModel;
            if (_vm != null)
            {
                _vm.TerminalDataReceived += OnTerminalData;
                _vm.LocalOutput          += OnTerminalData;
                Terminal.UserInput += s => _vm.SendData(s);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Terminal.Focus();
            // Auto-connect exactly once on first load (flag cleared immediately).
            if (_vm?.AutoConnectOnLoad == true)
            {
                _vm.AutoConnectOnLoad = false;
                _ = _vm.ConnectAsync();
            }
        }

        private void OnTerminalData(string data)
        {
            Terminal.AppendAnsiData(data);
        }
    }
}
