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
                // Unsubscribe from the previous VM to prevent a memory leak.
                if (e.OldValue is AiChatViewModel oldVm)
                    oldVm.MessagesChanged -= ScrollToBottom;
                if (e.NewValue is AiChatViewModel newVm)
                    newVm.MessagesChanged += ScrollToBottom;
            };
        }

        private void ScrollToBottom()
        {
            Dispatcher.Invoke(() => MessagesScroller.ScrollToEnd());
        }
    }
}
