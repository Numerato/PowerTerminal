using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Terminal.Controls;
using PowerTerminal.Models;
using PowerTerminal.Services;
using PowerTerminal.ViewModels;
using PowerTerminal.Views;

namespace PowerTerminal
{
    public partial class MainWindow : Window
    {
        internal MainViewModel Vm { get; } = new MainViewModel();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = Vm;

            Vm.OpenConnectionManagerRequested += OpenConnectionManager;
            Vm.OpenWikiEditorRequested        += OpenWikiEditor;
            Vm.VariablePromptRequested        += PromptVariable;

            StateChanged += (_, _) => UpdateMaxRestoreIcon();

            // Maximize on startup
            WindowState = WindowState.Normal;

            // Watch panel open/close to resize the panel column to saved width or 0
            Vm.PropertyChanged += OnVmPropertyChanged;

            // Splitter starts hidden (no panel open); terminal gets symmetric margins
            ClosePanel();

            // Block F1 help dialog
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Help,
                (_, e) => e.Handled = true));

            // Rebuild taskbar jump list whenever a connection is added, removed or replaced.
            Vm.ConnectionManager.Connections.CollectionChanged +=
                (_, _) => App.RebuildJumpList(Vm.ConnectionManager.Connections);

            // Handle --connect <guid> from a taskbar jump list click.
            Loaded += OnWindowLoaded;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnWindowLoaded;

            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--connect", StringComparison.OrdinalIgnoreCase)
                    && Guid.TryParse(args[i + 1], out var id))
                {
                    var conn = Vm.ConnectionManager.Connections
                                 .FirstOrDefault(c => c.Id == id);
                    if (conn != null)
                        Vm.ConnectToConnection(conn);
                    break;
                }
            }
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MainViewModel.IsPanelOpen)) return;

            if (Vm.IsPanelOpen)
            {
                var settings = new ConfigService().LoadSettings();
                double savedWidth = settings.SidepaneWidth > 50 ? settings.SidepaneWidth : 400;
                OpenPanel(savedWidth);
            }
            else
            {
                ClosePanel();
            }
        }

        /// <summary>
        /// Opens the side panel using star-based column sizing so WPF's layout engine
        /// automatically honours the terminal's MinWidth at any window size.
        /// The sidebar now lives in the outer grid's own 44 px column, so the content
        /// grid only needs to account for the splitter (6 px).
        /// </summary>
        private void OpenPanel(double desiredPanelPx)
        {
            // Available flexible width = content-area width minus splitter (6 px).
            // The sidebar (44 px) is in the outer grid — not our concern here.
            double available = Math.Max(1, ActualWidth - 44 - 6);

            // Ensure the terminal keeps at least its MinWidth (200) + left margin (6).
            double panelPx = Math.Min(desiredPanelPx, available - 200 - 6);
            panelPx = Math.Max(100, panelPx);

            // Express as a star ratio relative to the terminal column (which stays at 1*).
            double terminalPx = Math.Max(1, available - panelPx);
            double panelStars = panelPx / terminalPx;

            // Always restore both columns to star sizing so they scale correctly on window resize.
            // (WPF GridSplitter converts star→pixel during drag; we counteract that here.)
            TerminalColumn.MinWidth = 200;
            TerminalColumn.Width    = new GridLength(1, GridUnitType.Star);
            PanelColumn.MinWidth    = 100;
            PanelColumn.MaxWidth    = double.PositiveInfinity;
            PanelColumn.Width       = new GridLength(panelStars, GridUnitType.Star);

            MainSplitter.Visibility = Visibility.Visible;
            TerminalBorder.Margin   = new Thickness(6, 6, 0, 6);
        }

        private void ClosePanel()
        {
            PanelColumn.MinWidth = 0;
            PanelColumn.MaxWidth = double.PositiveInfinity;
            PanelColumn.Width    = new GridLength(0, GridUnitType.Pixel);

            MainSplitter.Visibility = Visibility.Collapsed;
            TerminalBorder.Margin   = new Thickness(6, 6, 6, 6);
        }

        private void Splitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            double width = PanelColumn.ActualWidth;
            if (width < 50) return; // panel being collapsed — don't save

            var configSvc = new ConfigService();
            var settings  = configSvc.LoadSettings();
            settings.SidepaneWidth = width;
            configSvc.SaveSettings(settings);

            // WPF GridSplitter converts star columns → pixel during the drag.
            // Re-apply star sizing so columns shrink proportionally on window resize.
            OpenPanel(width);
        }

        // F1 generates a routed Help command that WPF processes via KeyDown tunnelling.
        // Intercept it here so it never fires, letting PreviewKeyDown in TerminalControl handle it.
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.F1 && FocusManager.GetFocusedElement(this) is TerminalControl)
            {
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        // ── Tab lifecycle ────────────────────────────────────────────────────

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TerminalTabViewModel tab)
            {
                var dlg = new DarkConfirmWindow("Close tab", $"Close \"{tab.Header}\"?") { Owner = this };
                if (dlg.ShowDialog() == true)
                    Vm.RemoveTab(tab);
            }
        }

        // ── Connect dropdown ─────────────────────────────────────────────────

        private void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Vm.ConnectionManager.Connections.Count == 0)
                OpenConnectionManager();
            else
                ConnectPopup.IsOpen = true;
        }

        private void ConnectItem_Click(object sender, RoutedEventArgs e)
        {
            ConnectPopup.IsOpen = false;
            if (sender is Button btn && btn.Tag is SshConnection conn)
                Vm.ConnectToConnection(conn);
        }

        private void ManageConnections_Click(object sender, RoutedEventArgs e)
        {
            ConnectPopup.IsOpen = false;
            OpenConnectionManager();
        }

        // ── Title bar buttons ────────────────────────────────────────────────

        private void Settings_Click(object sender, RoutedEventArgs e)
            => Vm.TogglePanelCommand.Execute("settings");

        private void Minimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
            => Close();

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (Vm.TerminalTabs.Count > 0)
            {
                var dlg = new DarkConfirmWindow(
                    "Close PowerTerminal",
                    $"There {(Vm.TerminalTabs.Count == 1 ? "is 1 active terminal" : $"are {Vm.TerminalTabs.Count} active terminals")}. Close anyway?")
                { Owner = this };
                if (dlg.ShowDialog() != true)
                    e.Cancel = true;
            }
        }

        private void UpdateMaxRestoreIcon()
        {
            bool maximized = WindowState == WindowState.Maximized;
            MaxRestoreIcon.Text      = maximized ? "\uE923" : "\uE922";
            MaxRestoreBtn.ToolTip    = maximized ? "Restore" : "Maximize";
        }

        // ── Dialog helpers ────────────────────────────────────────────────────

        private void OpenConnectionManager()
        {
            var win = new ConnectionManagerWindow(Vm.ConnectionManager) { Owner = this };
            win.ShowDialog();
            if (win.DialogResult == true && Vm.ConnectionManager.Selected != null)
                Vm.ConnectSelectedCommand.Execute(null);
        }


        private void OpenWikiEditor(WikiEditorViewModel vm)
        {
            var win = new WikiEditorWindow(vm) { Owner = this };
            win.ShowDialog();
        }

        private string? PromptVariable(string name)
        {
            var dlg = new VariablePromptWindow(name) { Owner = this };
            return dlg.ShowDialog() == true ? dlg.Value : null;
        }

        // ── Maximize fix: prevent window from covering the taskbar ───────────

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwndSource = (HwndSource)PresentationSource.FromVisual(this);
            hwndSource?.AddHook(WndProc);
        }

        private const int WM_GETMINMAXINFO  = 0x0024;
        private const int WM_SIZING         = 0x0214;
        private const int WM_SYSKEYDOWN     = 0x0104;
        private const int VK_F10            = 0x79;

        // WMSZ edges
        private const int WMSZ_TOP         = 3;
        private const int WMSZ_TOPLEFT     = 4;
        private const int WMSZ_TOPRIGHT    = 5;
        private const int WMSZ_BOTTOM      = 6;
        private const int WMSZ_BOTTOMLEFT  = 7;
        private const int WMSZ_BOTTOMRIGHT = 8;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            else if (msg == WM_SIZING)
            {
                SnapHeightToLineGrid(wParam, lParam);
                handled = false; // let WPF continue normally
            }
            else if (msg == WM_SYSKEYDOWN && wParam.ToInt32() == VK_F10)
            {
                if (FocusManager.GetFocusedElement(this) is TerminalControl)
                    handled = true;
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// During WM_SIZING snap the window height so the terminal always shows
        /// whole lines — no half-visible bottom row.
        /// </summary>
        private void SnapHeightToLineGrid(IntPtr wParam, IntPtr lParam)
        {
            int edge = wParam.ToInt32();
            // Only snap height when resizing a vertical edge.
            // For WMSZ_LEFT (1) and WMSZ_RIGHT (2) the user is only changing
            // the width — don't touch the height at all.
            const int WMSZ_LEFT  = 1;
            const int WMSZ_RIGHT = 2;
            if (edge == WMSZ_LEFT || edge == WMSZ_RIGHT) return;
            // Find the active terminal control to read its char height
            double charHeight = GetActiveTerminalCharHeight();
            if (charHeight <= 1) return;

            var dpi = VisualTreeHelper.GetDpi(this);
            double dpiScale = dpi.DpiScaleY;

            // Chrome overhead in physical pixels:
            //   title bar (34 WPF) + top margin (6 WPF) + bottom margin (6 WPF) + 1px border × 2
            double chromeWpf = 34 + 6 + 6 + 2;
            double chromePx  = chromeWpf * dpiScale;
            double lineHeightPx = charHeight * dpiScale;

            var rect = Marshal.PtrToStructure<RECT>(lParam);
            int totalPx = rect.bottom - rect.top;

            // How many whole lines fit in the content area?
            double contentPx = totalPx - chromePx;
            int lines = Math.Max(1, (int)Math.Round(contentPx / lineHeightPx));
            int snappedTotal = (int)Math.Round(lines * lineHeightPx + chromePx);

            bool topEdge = edge == WMSZ_TOP || edge == WMSZ_TOPLEFT || edge == WMSZ_TOPRIGHT;

            if (topEdge)
                rect.top = rect.bottom - snappedTotal;
            else
                rect.bottom = rect.top + snappedTotal;

            Marshal.StructureToPtr(rect, lParam, true);
        }

        /// <summary>Returns the _charHeight of the first visible TerminalControl, or 0.</summary>
        private double GetActiveTerminalCharHeight()
        {
            var tab = Vm.ActiveTerminalTab;
            if (tab == null) return 0;

            // Walk the visual tree from the window to find a TerminalControl
            return FindTerminalCharHeight(this);
        }

        private static double FindTerminalCharHeight(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TerminalControl tc)
                    return tc.CharHeight;
                double found = FindTerminalCharHeight(child);
                if (found > 0) return found;
            }
            return 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                GetMonitorInfo(monitor, ref info);

                var work    = info.rcWork;
                var monitor2 = info.rcMonitor;

                mmi.ptMaxPosition.x = Math.Abs(work.left   - monitor2.left);
                mmi.ptMaxPosition.y = Math.Abs(work.top    - monitor2.top);
                mmi.ptMaxSize.x     = Math.Abs(work.right  - work.left);
                mmi.ptMaxSize.y     = Math.Abs(work.bottom - work.top);

                // Scale the WPF DIP minimums (960×560) to physical pixels so the
                // OS minimum matches WPF's MinWidth/MinHeight at any DPI setting.
                var dpi  = VisualTreeHelper.GetDpi(this);
                mmi.ptMinTrackSize.x = (int)Math.Ceiling(960 * dpi.DpiScaleX);
                mmi.ptMinTrackSize.y = (int)Math.Ceiling(560 * dpi.DpiScaleY);
            }

            Marshal.StructureToPtr(mmi, lParam, true);
        }
    }
}
