using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PowerTerminal.ViewModels;

namespace PowerTerminal.Views
{
    public partial class AiChatView : UserControl
    {
        public AiChatView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (DataContext is AiChatViewModel vm)
                    vm.MessagesChanged += ScrollToBottom;
            };
        }

        private void ScrollToBottom()
        {
            Dispatcher.Invoke(() => MessagesScroller.ScrollToEnd());
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                if (DataContext is AiChatViewModel vm && vm.SendCommand.CanExecute(null))
                    vm.SendCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
