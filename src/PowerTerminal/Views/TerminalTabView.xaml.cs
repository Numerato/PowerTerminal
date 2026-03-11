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
            DataContextChanged  += OnDataContextChanged;
            Loaded              += OnLoaded;
            IsVisibleChanged    += OnIsVisibleChanged;
        }

        private TerminalTabViewModel? _vm;
        // Stored so the same delegate can be unsubscribed when the VM changes
        private Action<string>? _userInputHandler;

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null)
            {
                _vm.TerminalDataReceived -= OnTerminalData;
                _vm.LocalOutput          -= OnTerminalData;
                _vm.ClearRequested       -= OnClearRequested;

                // Unsubscribe the stored UserInput handler so it doesn't accumulate
                if (_userInputHandler != null)
                {
                    Terminal.UserInput -= _userInputHandler;
                    _userInputHandler   = null;
                }

                // Cancel any pending password prompt — unblocks the SSH background thread
                Terminal.CancelHiddenInput();
            }

            _vm = DataContext as TerminalTabViewModel;
            if (_vm != null)
            {
                _vm.TerminalDataReceived += OnTerminalData;
                _vm.LocalOutput          += OnTerminalData;
                _vm.ClearRequested       += OnClearRequested;

                _userInputHandler = s => _vm.SendData(s);
                Terminal.UserInput += _userInputHandler;

                // Inline password collection: blocks the SSH background thread via MRE
                // until the user types a password and presses Enter in this terminal.
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
            if (_vm?.IsActive == true) Terminal.Focus();
            // Auto-connect exactly once on first load (flag cleared immediately).
            if (_vm?.AutoConnectOnLoad == true)
            {
                _vm.AutoConnectOnLoad = false;
                _ = _vm.ConnectAsync();
            }
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Give the terminal focus whenever this tab becomes the visible one
            if (e.NewValue is true)
                Terminal.Focus();
        }

        private void OnTerminalData(string data)
        {
            Terminal.AppendAnsiData(data);
        }

        private void OnClearRequested()
        {
            Terminal.ClearScreen();
        }
    }
}
