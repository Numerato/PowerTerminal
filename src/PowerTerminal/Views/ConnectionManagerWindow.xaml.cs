using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
            PopulatePredefinedIcons();
        }

        private ConnectionManagerViewModel Vm => (ConnectionManagerViewModel)DataContext;

        /// <summary>
        /// Fills the predefined-icons grid with every PNG/ICO/JPG found in the
        /// application's <c>icons/</c> folder (ships with the application).
        /// </summary>
        private void PopulatePredefinedIcons()
        {
            string iconsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icons");
            if (!Directory.Exists(iconsDir)) return;

            var files = Directory
                .GetFiles(iconsDir, "*.*")
                .Where(f =>
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext is ".png" or ".jpg" or ".jpeg" or ".ico";
                })
                .OrderBy(f => string.Equals(Path.GetFileName(f), "linux.png",
                                            StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(f => Path.GetFileName(f));

            PredefinedIconsList.ItemsSource = files.ToList();
        }

        private void PredefinedIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path && Vm.Editing != null)
                Vm.Editing.LogoPath = path;
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
