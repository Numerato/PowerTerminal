using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PowerTerminal.Models;
using PowerTerminal.ViewModels;

namespace PowerTerminal.Views
{
    public partial class ConnectionManagerWindow : Window
    {
        public ConnectionManagerWindow(ConnectionManagerViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            SyncIconComboBox();
            // Re-sync the icon ComboBox whenever a different connection is loaded into the editor
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ConnectionManagerViewModel.Editing))
                {
                    SyncIconComboBox();
                    if (vm.Editing != null && vm.IsAddMode)
                        FocusNameAfterLayout();
                }
            };
            // Focus the Name field after Copy or re-Add (fired from ViewModel)
            vm.FocusNameFieldRequested += FocusNameAfterLayout;
            // On first render: let the VM decide if it needs to auto-start Add.
            Loaded += (_, _) =>
            {
                vm.OnWindowShown();
                if (vm.IsAddMode)
                    FocusNameAfterLayout();
            };
        }

        /// <summary>
        /// Queues two nested Background-priority dispatches so the Name TextBox
        /// gets keyboard focus after all layout and rendering passes complete.
        /// </summary>
        private void FocusNameAfterLayout()
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    NameTextBox.Focus();
                    System.Windows.Input.Keyboard.Focus(NameTextBox);
                    NameTextBox.SelectAll();
                }))));
        }

        private ConnectionManagerViewModel Vm => (ConnectionManagerViewModel)DataContext;

        // ── Drag-and-drop reorder ────────────────────────────────────────────

        private Point _dragStart;
        private SshConnection _dragItem;

        private void ConnectionList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;
            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            DragDrop.DoDragDrop(ConnectionListBox, _dragItem, DragDropEffects.Move);
            _dragItem = null;
        }

        private void ConnectionList_Drop(object sender, DragEventArgs e)
        {
            var dragged = e.Data.GetData(typeof(SshConnection)) as SshConnection;
            if (dragged == null) return;

            // Find the item under the cursor
            var target = GetListBoxItemAt(e.GetPosition(ConnectionListBox));
            if (target == null || ReferenceEquals(target, dragged)) return;

            Vm.MoveConnection(dragged, target);
        }

        private SshConnection GetListBoxItemAt(Point pos)
        {
            var element = ConnectionListBox.InputHitTest(pos) as DependencyObject;
            while (element != null)
            {
                if (element is System.Windows.Controls.ListBoxItem item)
                    return item.DataContext as SshConnection;
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
            return null;
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            _dragStart = e.GetPosition(null);
            // Only initiate drag from a ListBoxItem
            var element = e.OriginalSource as DependencyObject;
            while (element != null)
            {
                if (element is System.Windows.Controls.ListBoxItem item)
                {
                    _dragItem = item.DataContext as SshConnection;
                    return;
                }
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
            _dragItem = null;
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)   => Vm.MoveSelectedUp();
        private void MoveDown_Click(object sender, RoutedEventArgs e) => Vm.MoveSelectedDown();

        // ── Dark chrome ──────────────────────────────────────────────────────

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseDialog_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ── Icon picker ──────────────────────────────────────────────────────

        /// <summary>
        /// Sync the ComboBox selection to match the current Editing.LogoPath.
        /// Called when the dialog opens and when Editing changes.
        /// </summary>
        private void SyncIconComboBox()
        {
            if (Vm?.Editing == null) return;
            var logoPath = Vm.Editing.LogoPath ?? string.Empty;
            // Match by relative path or by filename (handles legacy full paths stored in config)
            var match = Vm.IconOptions.FirstOrDefault(o =>
                string.Equals(o.Path, logoPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(o.Path), Path.GetFileName(logoPath), StringComparison.OrdinalIgnoreCase));
            IconComboBox.SelectedItem = match;
        }

        private void IconComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (IconComboBox.SelectedItem is IconOption option && Vm.Editing != null)
                Vm.Editing.LogoPath = option.Path;
        }

        private void BrowseLogo_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Select tab logo image",
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true && Vm.Editing != null)
            {
                Vm.Editing.LogoPath = dlg.FileName;
                // If the file is in the ico folder, try to select it in the combo
                SyncIconComboBox();
            }
        }

        // ── Delete with confirmation ─────────────────────────────────────────

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.Selected == null) return;
            var confirm = new DarkConfirmWindow(
                "Delete Connection",
                $"Delete \"{Vm.Selected.Name}\"?",
                defaultNo: true)
            {
                Owner = this
            };
            if (confirm.ShowDialog() == true)
                Vm.DeleteCommand.Execute(null);
        }

        // ── Save with confirmation ───────────────────────────────────────────

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.Editing == null) return;

            var missing = new System.Collections.Generic.List<string>();
            if (string.IsNullOrWhiteSpace(Vm.Editing.Name))     missing.Add("Name");
            if (string.IsNullOrWhiteSpace(Vm.Editing.Host))     missing.Add("Host / IP");
            if (string.IsNullOrWhiteSpace(Vm.Editing.Username)) missing.Add("Username");
            if (Vm.Editing.Port <= 0 || Vm.Editing.Port > 65535) missing.Add("Port (1–65535)");

            if (missing.Count > 0)
            {
                DarkMessageBox.Show(
                    $"Please fill in the required fields:\n\n• {string.Join("\n• ", missing)}",
                    "Required Fields Missing",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            var confirm = new DarkConfirmWindow(
                "Save Connection",
                $"Save changes to \"{Vm.Editing.Name}\"?")
            {
                Owner = this
            };
            if (confirm.ShowDialog() == true)
                Vm.SaveCommand.Execute(null);
        }

        // ── Connect / close ──────────────────────────────────────────────────

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
