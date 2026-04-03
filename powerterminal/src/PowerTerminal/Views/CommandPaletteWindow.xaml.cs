using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PowerTerminal.Models;
using PowerTerminal.ViewModels;

namespace PowerTerminal.Views
{
    public partial class CommandPaletteWindow : Window
    {
        private bool _closing;

        private static readonly SolidColorBrush AccentBrush     = new(Color.FromRgb(0xE8, 0x77, 0x22));
        private static readonly SolidColorBrush PromptVarBrush  = new(Color.FromRgb(0xE8, 0xA4, 0x5A));
        private static readonly SolidColorBrush SysVarBrush     = new(Color.FromRgb(0x7E, 0xC8, 0xA0));
        private static readonly SolidColorBrush CommandBrush    = new(Color.FromRgb(0x7E, 0xC8, 0xA0));
        private static readonly SolidColorBrush DimBrush        = new(Color.FromRgb(0x44, 0x44, 0x44));

        private static readonly SolidColorBrush PromptVarBg     = new(Color.FromArgb(0x22, 0xE8, 0xA4, 0x5A));
        private static readonly SolidColorBrush SysVarBg        = new(Color.FromArgb(0x22, 0x7E, 0xC8, 0xA0));

        /// <summary>Fired when a command is selected for sending to the terminal.</summary>
        public event Action<string>? CommandSelected;

        private CommandPaletteViewModel ViewModel => (CommandPaletteViewModel)DataContext;

        public CommandPaletteWindow()
        {
            InitializeComponent();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            SearchBox.Focus();
            UpdateChipStyles();
            UpdateResultCount();
            UpdateDetail();

            // Re-render command lines whenever the filtered list changes
            ViewModel.FilteredCommands.CollectionChanged += (_, _) =>
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, (Action)(() =>
                {
                    RenderCommandLines();
                    UpdateResultCount();
                    UpdateDetail();
                }));

            // Initial render (items are already loaded)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                (Action)RenderCommandLines);
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (!_closing) { _closing = true; Close(); }
        }

        // ── Keyboard (window-level, intercepts before any child) ─────────────

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                    MoveSelection(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    MoveSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    SendSelected(withEnter: true);
                    e.Handled = true;
                    break;
                case Key.Tab:
                    SendSelected(withEnter: false);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (!_closing) { _closing = true; Close(); }
                    e.Handled = true;
                    break;
            }
        }

        // ── Category chips ────────────────────────────────────────────────────

        private void Chip_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string tag)
            {
                ViewModel.ActiveTag = (tag == "All") ? string.Empty : tag;
                UpdateChipStyles();
                UpdateResultCount();
                UpdateDetail();
                SearchBox.Focus();
            }
        }

        private void UpdateChipStyles()
        {
            if (ChipPanel == null) return;
            foreach (var item in ChipPanel.Items)
            {
                var container = ChipPanel.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                var border = container?.FindName("ChipBorder") as Border
                             ?? FindVisualChild<Border>(container);
                var text   = border?.FindName("ChipText") as TextBlock
                             ?? FindVisualChild<TextBlock>(border);
                if (border == null) continue;

                bool isActive = string.Equals(item?.ToString(), ViewModel.ActiveTag, StringComparison.OrdinalIgnoreCase)
                                || (item?.ToString() == "All" && string.IsNullOrEmpty(ViewModel.ActiveTag));

                border.BorderBrush = isActive ? AccentBrush : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                border.Background  = isActive ? new SolidColorBrush(Color.FromArgb(0x18, 0xE8, 0x77, 0x22))
                                              : Brushes.Transparent;
                if (text != null)
                    text.Foreground = isActive ? AccentBrush : new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            }
        }

        // ── Results ───────────────────────────────────────────────────────────

        private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDetail();
            RenderCommandLines();
        }

        private void ResultsList_Click(object sender, MouseButtonEventArgs e)
        {
            if (ResultsList.SelectedItem != null)
                SendSelected(withEnter: true);
        }

        private void UpdateResultCount()
        {
            if (ResultCount != null)
            {
                int n = ViewModel.FilteredCommands.Count;
                ResultCount.Text = n > 0 ? $"{n} command{(n == 1 ? "" : "s")}" : string.Empty;
            }
        }

        private void MoveSelection(int delta)
        {
            int count = ResultsList.Items.Count;
            if (count == 0) return;
            int idx = ResultsList.SelectedIndex + delta;
            idx = Math.Max(0, Math.Min(count - 1, idx));
            ResultsList.SelectedIndex = idx;
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
        }

        // ── Command line rich rendering (variable segments) ───────────────────

        private void RenderCommandLines()
        {
            if (ViewModel == null) return;

            // Walk the visual tree and fill in CmdLine TextBlocks for each visible item
            foreach (LinuxCommand cmd in ViewModel.FilteredCommands)
            {
                var container = ResultsList.ItemContainerGenerator.ContainerFromItem(cmd) as ListBoxItem;
                if (container == null) continue;
                var cmdLine = FindVisualChild<TextBlock>(container, "CmdLine");
                if (cmdLine == null) continue;
                RenderSegments(cmdLine, ViewModel.GetCommandSegments(cmd), isDetail: false);
            }
        }

        // ── Detail strip ──────────────────────────────────────────────────────

        private void UpdateDetail()
        {
            var cmd = ViewModel?.SelectedCommand;
            if (cmd == null) { DetailStrip.Visibility = Visibility.Collapsed; return; }

            DetailStrip.Visibility = Visibility.Visible;

            // Full command with variable segments
            RenderSegments(DetailCmdBlock, ViewModel.GetCommandSegments(cmd), isDetail: true);
            DetailDescBlock.Text = cmd.Description;

            // Variable legend
            VarLegend.Children.Clear();
            bool hasPrompt = false, hasSys = false;
            foreach (var (_, kind) in ViewModel.GetCommandSegments(cmd))
            {
                if (kind == "prompt") hasPrompt = true;
                if (kind == "sys")    hasSys    = true;
            }
            if (hasPrompt) AddLegendDot(PromptVarBrush, "needs input");
            if (hasSys)    AddLegendDot(SysVarBrush,    "auto-filled from machine");
        }

        private void AddLegendDot(SolidColorBrush color, string label)
        {
            VarLegend.Children.Add(new Ellipse
            {
                Width  = 7, Height = 7, Fill = color,
                Margin = new Thickness(0, 0, 3, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            VarLegend.Children.Add(new TextBlock
            {
                Text       = label,
                FontSize   = 10.5,
                Foreground = DimBrush,
                Margin     = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        private static void RenderSegments(TextBlock tb, List<(string text, string kind)> segments, bool isDetail)
        {
            tb.Inlines.Clear();
            foreach (var (text, kind) in segments)
            {
                if (kind == "sys")
                {
                    var run = new Run(text) { Foreground = SysVarBrush };
                    if (isDetail)
                        run.Background = SysVarBg;
                    tb.Inlines.Add(run);
                }
                else if (kind == "prompt")
                {
                    var run = new Run(text)
                    {
                        Foreground  = PromptVarBrush,
                        FontStyle   = FontStyles.Italic,
                    };
                    if (isDetail)
                        run.Background = PromptVarBg;
                    tb.Inlines.Add(run);
                }
                else
                {
                    tb.Inlines.Add(new Run(text) { Foreground = CommandBrush });
                }
            }
        }

        // ── Send selected command ─────────────────────────────────────────────

        private void SendSelected(bool withEnter)
        {
            var cmd = ViewModel?.SelectedCommand;
            if (cmd == null) return;

            // Start from system-vars-resolved command; prompt vars still contain $name$
            string resolved = ViewModel.ResolvedCommand(cmd);

            // Collect unique prompt variables (those not resolved by system vars)
            var promptVars = Regex
                .Matches(resolved, @"\$([a-z0-9_]+)\$", RegexOptions.IgnoreCase)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var varName in promptVars)
            {
                var dlg = new VariablePromptWindow(varName) { Owner = this };
                if (dlg.ShowDialog() != true) return; // user cancelled — abort send

                string value = dlg.Value;
                resolved = Regex.Replace(
                    resolved,
                    @"\$" + Regex.Escape(varName) + @"\$",
                    value.Replace("$", "$$"), // literal $ in replacement
                    RegexOptions.IgnoreCase);
            }

            _closing = true;
            CommandSelected?.Invoke(withEnter ? resolved + "\r" : resolved);
            Close();
        }

        // ── Visual tree helpers ───────────────────────────────────────────────

        private static T? FindVisualChild<T>(DependencyObject? parent, string? name = null)
            where T : FrameworkElement
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t && (name == null || t.Name == name)) return t;
                var result = FindVisualChild<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }
    }
}
