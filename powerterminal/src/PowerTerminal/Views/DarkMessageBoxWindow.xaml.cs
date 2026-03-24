using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PowerTerminal.Views
{
    public partial class DarkMessageBoxWindow : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        public DarkMessageBoxWindow(
            string title, string message,
            MessageBoxButton buttons, MessageBoxImage icon,
            MessageBoxResult defaultResult = MessageBoxResult.None)
        {
            InitializeComponent();
            TitleText.Text   = title;
            MessageText.Text = message;
            ApplyIcon(icon);
            ApplyButtons(buttons, defaultResult);
        }

        private void ApplyIcon(MessageBoxImage icon)
        {
            switch (icon)
            {
                case MessageBoxImage.Error:
                    IconText.Text       = "\uEA39"; // StatusCircleErrorX
                    IconText.Foreground = (Brush)FindResource("ErrorBrush");
                    IconText.Visibility = Visibility.Visible;
                    break;
                case MessageBoxImage.Warning:
                    IconText.Text       = "\uE7BA"; // Warning
                    IconText.Foreground = (Brush)FindResource("WarningBrush");
                    IconText.Visibility = Visibility.Visible;
                    break;
                case MessageBoxImage.Information:
                    IconText.Text       = "\uE946"; // Info
                    IconText.Foreground = (Brush)FindResource("AccentBrush");
                    IconText.Visibility = Visibility.Visible;
                    break;
                case MessageBoxImage.Question:
                    IconText.Text       = "\uE897"; // Help
                    IconText.Foreground = (Brush)FindResource("AccentBrush");
                    IconText.Visibility = Visibility.Visible;
                    break;
                // MessageBoxImage.None → icon stays Collapsed
            }
        }

        private void ApplyButtons(MessageBoxButton buttons, MessageBoxResult defaultResult)
        {
            switch (buttons)
            {
                case MessageBoxButton.OK:
                    OkButton.Visibility = Visibility.Visible;
                    break;
                case MessageBoxButton.OKCancel:
                    OkButton.Visibility     = Visibility.Visible;
                    CancelButton.Visibility = Visibility.Visible;
                    break;
                case MessageBoxButton.YesNo:
                    YesButton.Visibility = Visibility.Visible;
                    NoButton.Visibility  = Visibility.Visible;
                    break;
                case MessageBoxButton.YesNoCancel:
                    YesButton.Visibility    = Visibility.Visible;
                    NoButton.Visibility     = Visibility.Visible;
                    CancelButton.Visibility = Visibility.Visible;
                    break;
            }

            // Apply IsDefault — fall back to the first visible affirmative button
            switch (defaultResult)
            {
                case MessageBoxResult.Yes:    YesButton.IsDefault = true;    break;
                case MessageBoxResult.No:     NoButton.IsDefault  = true;    break;
                case MessageBoxResult.Cancel: CancelButton.IsDefault = true; break;
                case MessageBoxResult.OK:     OkButton.IsDefault  = true;    break;
                default:
                    if      (OkButton.Visibility  == Visibility.Visible) OkButton.IsDefault  = true;
                    else if (YesButton.Visibility == Visibility.Visible) YesButton.IsDefault = true;
                    break;
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Yes_Click(object sender, RoutedEventArgs e)
            { Result = MessageBoxResult.Yes;    Close(); }

        private void Ok_Click(object sender, RoutedEventArgs e)
            { Result = MessageBoxResult.OK;     Close(); }

        private void No_Click(object sender, RoutedEventArgs e)
            { Result = MessageBoxResult.No;     Close(); }

        private void Cancel_Click(object sender, RoutedEventArgs e)
            { Result = MessageBoxResult.Cancel; Close(); }

        // The X button on the title bar — maps to Cancel for YesNoCancel,
        // No for YesNo, and Cancel/None otherwise.
        private void Dismiss_Click(object sender, RoutedEventArgs e)
        {
            if (CancelButton.Visibility == Visibility.Visible)
                Result = MessageBoxResult.Cancel;
            else if (NoButton.Visibility == Visibility.Visible)
                Result = MessageBoxResult.No;
            else
                Result = MessageBoxResult.None;
            Close();
        }
    }
}
