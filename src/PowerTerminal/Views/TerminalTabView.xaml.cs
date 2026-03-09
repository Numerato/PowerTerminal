using System.Threading;
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

                // Inline password collection: blocks the SSH background thread via MRE
                // until the user types a password and presses Enter in the terminal.
                _vm.InlinePasswordCollector = prompt =>
                {
                    var mre    = new ManualResetEventSlim(false);
                    var result = string.Empty;
                    Dispatcher.Invoke(() =>
                    {
                        Terminal.CollectHiddenInput(prompt, pw =>
                        {
                            result = pw;
                            mre.Set();
                        });
                    });
                    mre.Wait();
                    return result;
                };

                // If the view is already in the visual tree (Loaded fired before DataContextChanged),
                // trigger auto-connect here so the connection attempt is never missed.
                if (IsLoaded && _vm.AutoConnectOnLoad)
                {
                    _vm.AutoConnectOnLoad = false;
                    _ = _vm.ConnectAsync();
                }
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
