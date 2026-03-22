using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using PowerTerminal.ViewModels;
using Terminal.Ssh;

namespace PowerTerminal.Views
{
    public partial class TerminalTabView : System.Windows.Controls.UserControl
    {
        public TerminalTabView()
        {
            InitializeComponent();
            DataContextChanged  += OnDataContextChanged;
            Loaded              += OnLoaded;
            IsVisibleChanged    += OnIsVisibleChanged;
        }

        private TerminalTabViewModel? _vm;
        private EventHandler<ISshTerminalSession>? _attachHandler;
        private EventHandler? _requestConnectHandler;
        private Action<string>? _localOutputHandler;

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null)
            {
                if (_attachHandler        != null) _vm.SessionAttachRequired -= _attachHandler;
                if (_requestConnectHandler != null) _vm.RequestConnect        -= _requestConnectHandler;
                if (_localOutputHandler   != null) _vm.LocalOutput            -= _localOutputHandler;
                _attachHandler         = null;
                _requestConnectHandler = null;
                _localOutputHandler    = null;
            }

            _vm = DataContext as TerminalTabViewModel;
            if (_vm == null) return;

            _attachHandler = (_, session) =>
            {
                Terminal.EnsureEmulatorInitialized();
                Terminal.AttachSession(session);
            };
            _vm.SessionAttachRequired += _attachHandler;

            _requestConnectHandler = (_, _) => _ = DoConnectAsync();
            _vm.RequestConnect += _requestConnectHandler;

            _localOutputHandler = text => Terminal.WriteStatusMessage(text);
            _vm.LocalOutput += _localOutputHandler;

            if (IsLoaded && _vm.AutoConnectOnLoad)
            {
                _vm.AutoConnectOnLoad = false;
                _ = DoConnectAsync();
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_vm?.IsActive == true) FocusTerminal();

            if (_vm?.AutoConnectOnLoad == true)
            {
                _vm.AutoConnectOnLoad = false;
                _ = DoConnectAsync();
            }
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
                FocusTerminal();
        }

        private void FocusTerminal()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                Terminal.Focus();
                Keyboard.Focus(Terminal);
            }));
        }

        private async System.Threading.Tasks.Task DoConnectAsync()
        {
            if (_vm == null) return;
            int cols = Terminal.TerminalColumns > 0 ? Terminal.TerminalColumns : 80;
            int rows = Terminal.TerminalRows    > 0 ? Terminal.TerminalRows    : 24;

            await _vm.ConnectAsync(
                (prompt, ct) => Terminal.PromptForPasswordAsync(prompt, ct),
                cols, rows);
        }

        private void Terminal_ScrollbackChanged(object sender, EventArgs e)
        {
            int count = Terminal.ScrollbackCount;
            TerminalScrollBar.Maximum    = count;
            TerminalScrollBar.LargeChange = Math.Max(1, Terminal.TerminalRows);
            TerminalScrollBar.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            TerminalScrollBar.Value      = Terminal.ScrollOffset;
        }

        private void TerminalScrollBar_Scroll(object sender, ScrollEventArgs e)
        {
            Terminal.ScrollOffset    = (int)Math.Round(e.NewValue);
            TerminalScrollBar.Value  = Terminal.ScrollOffset;
        }
    }
}