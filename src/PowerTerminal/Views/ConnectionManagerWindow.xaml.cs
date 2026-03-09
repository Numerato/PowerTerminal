using System.Windows;
using Microsoft.Win32;
using PowerTerminal.ViewModels;

namespace PowerTerminal.Views
{
    public partial class ConnectionManagerWindow : Window
    {
        public ConnectionManagerWindow(ConnectionManagerViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }

        private ConnectionManagerViewModel Vm => (ConnectionManagerViewModel)DataContext;

        private void BrowseKey_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Select private key file",
                Filter = "PEM files (*.pem)|*.pem|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true && Vm.Editing != null)
                Vm.Editing.PrivateKeyPath = dlg.FileName;
        }

        private void ClearKey_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.Editing != null)
                Vm.Editing.PrivateKeyPath = null;
        }

        private void BrowseLogo_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Select tab logo image",
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true && Vm.Editing != null)
                Vm.Editing.LogoPath = dlg.FileName;
        }

        private void ClearLogo_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.Editing != null)
                Vm.Editing.LogoPath = null;
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
