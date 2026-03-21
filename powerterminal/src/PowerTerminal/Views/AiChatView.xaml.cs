using System.Windows.Controls;
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
    }
}
