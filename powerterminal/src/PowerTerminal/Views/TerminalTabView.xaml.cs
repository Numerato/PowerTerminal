using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using PowerTerminal.ViewModels;
using Terminal.Ssh;

// ReSharper disable once CheckNamespace — PowerTerminal.Views sub-namespace intentional
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
        private EventHandler<string>? _powerEditHandler;
        private Action<string, bool>? _explorerOpenHandler;

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
                if (_powerEditHandler  != null) Terminal.PowerEditCommand -= _powerEditHandler;
                _powerEditHandler      = null;
                if (_explorerOpenHandler != null) _vm.ExplorerOpenFileRequested -= _explorerOpenHandler;
                _explorerOpenHandler   = null;
            }

            _vm = DataContext as TerminalTabViewModel;
            if (_vm == null) return;

            Terminal.EnablePowerEdit = _vm.EnablePowerEdit;
            Terminal.CopyPasteMode   = (Terminal.Controls.TerminalCopyPasteMode)_vm.CopyPasteMode;

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

            if (_vm.EnablePowerEdit)
            {
                _powerEditHandler = (_, cmd) => OnPowerEditCommand(cmd);
                Terminal.PowerEditCommand += _powerEditHandler;

                _explorerOpenHandler = (path, useSudo) => _ = OpenFileFromExplorerAsync(path, useSudo);
                _vm.ExplorerOpenFileRequested += _explorerOpenHandler;
            }

            if (IsLoaded && _vm.AutoConnectOnLoad)
            {
                _vm.AutoConnectOnLoad = false;
                _ = DoConnectAsync();
            }
        }

        private async void OnPowerEditCommand(string cmd)
        {
            try
            {
                await OnPowerEditCommandAsync(cmd);
            }
            catch (Exception ex)
            {
                Terminal.WriteStatusMessage($"\r\npwe: error: {ex.Message}\r\n");
                _vm?.SendData("\r");
            }
        }

        private async System.Threading.Tasks.Task OnPowerEditCommandAsync(string cmd)
        {
            string filename = cmd.Length > "pwe ".Length
                ? cmd["pwe ".Length..].Trim()
                : string.Empty;

            if (string.IsNullOrWhiteSpace(filename))
            {
                Terminal.WriteStatusMessage("\r\npwe: Usage: pwe <filename>\r\n");
                _vm?.SendData("\r");
                return;
            }

            // Resolve path: join with CWD from the shell prompt if the path is relative
            string resolvedPath = ResolvePath(filename);

            // Write command to bash history file silently via exec channel (no shell stream echo)
            string escaped = cmd.Replace("'", "'\\''");
            if (_vm != null)
                _ = _vm.RunCommandAsync($"printf '%s\\n' '{escaped}' >> ~/.bash_history");

            // Check if file exists first
            string fileExistsStr = _vm != null ? await _vm.RunCommandAsync($"[ -e \"{EscapePath(resolvedPath)}\" ] && echo yes || echo no") : "no";
            bool   fileExists    = fileExistsStr.Trim() == "yes";

            bool   useSudo      = false;
            bool   isReadOnly   = false;
            string sudoPassword = string.Empty;
            string content      = string.Empty;
            var    ct           = System.Threading.CancellationToken.None;

            if (!fileExists)
            {
                // New file — check if parent directory is writable
                string parentDir       = _vm != null ? await _vm.RunCommandAsync($"dirname \"{EscapePath(resolvedPath)}\"") : string.Empty;
                string parentWriteStr  = _vm != null ? await _vm.RunCommandAsync($"[ -w \"{EscapePath(parentDir.Trim())}\" ] && echo yes || echo no") : "no";
                bool   parentWritable  = parentWriteStr.Trim() == "yes";

                if (!parentWritable)
                {
                    string user = _vm?.Username ?? "user";
                    string mode = await Terminal.PromptForInputAsync(
                        "No write permission to directory. Create with sudo or cancel? (s/c): ", ct);

                    if (!mode.Trim().Equals("s", StringComparison.OrdinalIgnoreCase))
                    {
                        _vm?.SendData("\r");
                        return;
                    }
                    sudoPassword = await Terminal.PromptForPasswordAsync($"[sudo] password for {user}: ", ct);
                    if (string.IsNullOrEmpty(sudoPassword))
                    {
                        _vm?.SendData("\r");
                        return;
                    }
                    useSudo = true;
                }
                // content stays empty — new file
            }
            else
            {
                // File exists — check permissions
                string canReadStr  = _vm != null ? await _vm.RunCommandAsync($"[ -r \"{EscapePath(resolvedPath)}\" ] && echo yes || echo no") : "no";
                string canWriteStr = _vm != null ? await _vm.RunCommandAsync($"[ -w \"{EscapePath(resolvedPath)}\" ] && echo yes || echo no") : "no";
                bool canRead  = canReadStr.Trim() == "yes";
                bool canWrite = canWriteStr.Trim() == "yes";

                if (!canRead)
                {
                    // No read permission — ask sudo password in the terminal
                    string user = _vm?.Username ?? "user";
                    sudoPassword = await Terminal.PromptForPasswordAsync($"[sudo] password for {user}: ", ct);
                    if (string.IsNullOrEmpty(sudoPassword))
                    {
                        _vm?.SendData("\r");
                        return;
                    }
                    useSudo = true;
                }
                else if (!canWrite)
                {
                    // Readable but not writable — ask mode visibly in the terminal
                    string mode = await Terminal.PromptForInputAsync(
                        "No write permission. Open read-only or edit with sudo? (r/e): ", ct);

                    if (string.IsNullOrWhiteSpace(mode) || mode.Trim().Equals("r", StringComparison.OrdinalIgnoreCase))
                    {
                        isReadOnly = true;
                    }
                    else if (mode.Trim().Equals("e", StringComparison.OrdinalIgnoreCase))
                    {
                        string user = _vm?.Username ?? "user";
                        sudoPassword = await Terminal.PromptForPasswordAsync($"[sudo] password for {user}: ", ct);
                        if (string.IsNullOrEmpty(sudoPassword))
                        {
                            _vm?.SendData("\r");
                            return;
                        }
                        useSudo = true;
                    }
                    else
                    {
                        // Cancelled or unrecognised input
                        _vm?.SendData("\r");
                        return;
                    }
                }

                // Read the file content
                var (readContent, success) = _vm != null
                    ? await _vm.ReadFileAsync(resolvedPath, useSudo ? sudoPassword : string.Empty)
                    : (string.Empty, false);

                if (!success)
                {
                    Terminal.WriteStatusMessage($"pwe: Cannot read file: {resolvedPath}\r\n");
                    _vm?.SendData("\r");
                    return;
                }
                content = readContent;
            }

            // Normalise line endings to \n — WPF TextBox stores \n internally.
            // Storing \r\n here would cause the TwoWay binding to push back a \n-only
            // version on first render, which would differ from \r\n, fire the Content
            // setter, and incorrectly set IsModified = true on a freshly opened file.
            content = content.Replace("\r\n", "\n").Replace("\r", "\n");

            // Build and wire up the editor ViewModel
            var editorVm = new PowerEditViewModel
            {
                FilePath     = resolvedPath,
                IsReadOnly   = isReadOnly,
                UseSudo      = useSudo,
                SudoPassword = sudoPassword,
                FontFamily   = _vm?.Theme?.FontFamily ?? "Consolas",
                FontSize     = _vm?.Theme?.FontSize   ?? 13,
                ReadFile     = (path, pass) => _vm!.ReadFileAsync(path, pass),
                WriteFile    = (path, cont, pass) => _vm!.WriteFileAsync(path, cont, pass)
            };
            editorVm.CloseRequested += (_, _) => _ = ConfirmAndCloseEditorAsync(editorVm);

            // Show the editor first (establishes DataContext + binding) then load content
            Dispatcher.Invoke(() =>
            {
                PowerEditor.DataContext = editorVm;
                PowerEditor.Visibility  = Visibility.Visible;
                Terminal.Visibility     = Visibility.Collapsed;
                TerminalScrollBar.Visibility = Visibility.Collapsed;
                editorVm.LoadContent(content);
                PowerEditor.FocusEditor();
            });
        }

        private async System.Threading.Tasks.Task WireEditorSaveErrorAsync(PowerEditViewModel vm)
        {
            // The ViewModel's SaveAsync is void-fire-and-forget from the menu;
            // we can't easily catch errors in RelayCommand. Instead we add an
            // override approach: nothing needed here for now — errors surface via
            // a MessageBox inside the RelayCommand if we wrap it.
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private async System.Threading.Tasks.Task ConfirmAndCloseEditorAsync(PowerEditViewModel vm)
        {
            if (vm.IsModified)
            {
                string filename = System.IO.Path.GetFileName(vm.FilePath);
                var result = DarkMessageBox.Show(
                    Window.GetWindow(this),
                    $"'{filename}' has unsaved changes.\n\nSave before closing?",
                    "PowerEdit",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel) return;

                if (result == MessageBoxResult.Yes)
                {
                    bool saved = await vm.SaveAsync();
                    if (!saved)
                    {
                        DarkMessageBox.Show(
                            Window.GetWindow(this),
                            "Save failed — file was not closed.",
                            "PowerEdit",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }
                }
                // MessageBoxResult.No → close without saving
            }

            CloseEditor();
        }

        private void CloseEditor()
        {
            Dispatcher.Invoke(() =>
            {
                PowerEditor.Visibility  = Visibility.Collapsed;
                PowerEditor.DataContext = null;
                Terminal.Visibility     = Visibility.Visible;
                TerminalScrollBar.Visibility = Terminal.ScrollbackCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                FocusTerminal();
            });
            // Bash already has a prompt from running the silent poweredit script — no \r needed
        }

        private string ResolvePath(string filename)
        {
            // If already absolute, use as-is
            if (filename.StartsWith("/") || filename.StartsWith("$HOME"))
                return filename;

            // Tilde is NOT expanded inside double quotes in bash — replace with $HOME
            if (filename.StartsWith("~"))
                return "$HOME" + filename[1..];

            // Try to parse the CWD from the terminal's current shell prompt line
            string cwd = Terminal.GetCurrentWorkingDir();
            if (!string.IsNullOrEmpty(cwd))
            {
                if (cwd.StartsWith("~"))
                    cwd = "$HOME" + cwd[1..];
                return cwd.TrimEnd('/') + "/" + filename;
            }

            // Fall back: same directory as SSH exec channel default ($HOME)
            return "$HOME/" + filename;
        }

        private static string EscapePath(string path)
            => path.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>Opens a remote file in the editor - called from the Explorer panel (no terminal prompts).</summary>
        public async System.Threading.Tasks.Task OpenFileFromExplorerAsync(string path, bool useSudo)
        {
            if (_vm == null) return;
            string sudoPassword = string.Empty;

            string existsStr = await _vm.RunCommandAsync($"[ -e \"{EscapePath(path)}\" ] && echo yes || echo no");
            bool exists = existsStr.Trim() == "yes";

            bool isReadOnly = false;
            string content = string.Empty;

            if (!exists)
            {
                string parentDir = (await _vm.RunCommandAsync($"dirname \"{EscapePath(path)}\"")).Trim();
                string writable  = await _vm.RunCommandAsync($"[ -w \"{EscapePath(parentDir)}\" ] && echo yes || echo no");
                if (writable.Trim() != "yes")
                {
                    if (!useSudo)
                    {
                        DarkMessageBox.Show(
                            Window.GetWindow(this),
                            $"No write permission to directory.\nEnable Sudo in the Explorer toolbar to create this file.",
                            "Permission Denied",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                    sudoPassword = ShowPasswordDialog();
                    if (string.IsNullOrEmpty(sudoPassword)) return;
                }
            }
            else
            {
                string canRead  = await _vm.RunCommandAsync($"[ -r \"{EscapePath(path)}\" ] && echo yes || echo no");
                string canWrite = await _vm.RunCommandAsync($"[ -w \"{EscapePath(path)}\" ] && echo yes || echo no");

                if (canRead.Trim() != "yes")
                {
                    if (!useSudo)
                    {
                        DarkMessageBox.Show(
                            Window.GetWindow(this),
                            $"No read permission for:\n{path}\n\nEnable Sudo in the Explorer toolbar.",
                            "Permission Denied",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                    sudoPassword = ShowPasswordDialog();
                    if (string.IsNullOrEmpty(sudoPassword)) return;
                }
                else if (canWrite.Trim() != "yes")
                {
                    // File is readable but not writable — ask user what to do
                    string choice = string.Empty;
                    Dispatcher.Invoke(() =>
                    {
                        var dlg = new DarkChoiceWindow(
                            "Read-only File",
                            $"You do not have write permission for:\n{path}\n\nHow do you want to open it?",
                            new[] { "Read Only", "Edit with Sudo", "Cancel" })
                        {
                            Owner = Window.GetWindow(this)
                        };
                        dlg.ShowDialog();
                        choice = dlg.SelectedChoice ?? "Cancel";
                    });

                    if (choice == "Cancel" || string.IsNullOrEmpty(choice)) return;
                    if (choice == "Read Only")
                    {
                        isReadOnly = true;
                    }
                    else // "Edit with Sudo"
                    {
                        sudoPassword = ShowPasswordDialog();
                        if (string.IsNullOrEmpty(sudoPassword)) return;
                        useSudo = true;
                    }
                }

                var (readContent, success) = await _vm.ReadFileAsync(path, sudoPassword);
                if (!success)
                {
                    DarkMessageBox.Show(
                        Window.GetWindow(this),
                        $"Cannot read file:\n{path}",
                        "Read Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
                content = readContent;
            }

            content = content.Replace("\r\n", "\n").Replace("\r", "\n");

            var editorVm = new PowerEditViewModel
            {
                FilePath     = path,
                IsReadOnly   = isReadOnly,
                UseSudo      = !string.IsNullOrEmpty(sudoPassword),
                SudoPassword = sudoPassword,
                FontFamily   = _vm?.Theme?.FontFamily ?? "Consolas",
                FontSize     = _vm?.Theme?.FontSize   ?? 13,
                ReadFile     = (p, pass) => _vm!.ReadFileAsync(p, pass),
                WriteFile    = (p, c, pass) => _vm!.WriteFileAsync(p, c, pass)
            };
            editorVm.CloseRequested += (_, _) => _ = ConfirmAndCloseEditorAsync(editorVm);

            Dispatcher.Invoke(() =>
            {
                PowerEditor.DataContext = editorVm;
                PowerEditor.Visibility  = Visibility.Visible;
                Terminal.Visibility     = Visibility.Collapsed;
                TerminalScrollBar.Visibility = Visibility.Collapsed;
                editorVm.LoadContent(content);
                PowerEditor.FocusEditor();
            });
        }

        private string ShowPasswordDialog()
        {
            string result = string.Empty;
            Dispatcher.Invoke(() =>
            {
                var dlg = new DarkPasswordWindow("Sudo Authentication", $"Enter sudo password for {_vm?.Username ?? "user"}:")
                {
                    Owner = Window.GetWindow(this)
                };
                if (dlg.ShowDialog() == true)
                    result = dlg.Password;
            });
            return result;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_vm?.IsActive == true) FocusActiveView();

            if (_vm?.AutoConnectOnLoad == true)
            {
                _vm.AutoConnectOnLoad = false;
                _ = DoConnectAsync();
            }

            // Inject "Create Command" into the terminal right-click context menu
            Terminal.ContextMenuOpening += Terminal_ContextMenuOpening;
        }

        private void TerminalMenu_Copy(object sender, RoutedEventArgs e)
        {
            var text = Terminal.GetSelectedText();
            if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
        }

        private void TerminalMenu_Paste(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText())
                _vm?.SendData(Clipboard.GetText());
        }

        private void Terminal_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var menu = Terminal.ContextMenu;
            if (menu == null) return;

            // Remove any previously injected separator+item
            for (int i = menu.Items.Count - 1; i >= 0; i--)
            {
                if (menu.Items[i] is MenuItem mi && mi.Tag is "create-command")
                    menu.Items.RemoveAt(i);
                else if (menu.Items[i] is Separator sep && sep.Tag is "create-command-sep")
                    menu.Items.RemoveAt(i);
            }

            string selectedText = Terminal.GetSelectedText();
            if (string.IsNullOrWhiteSpace(selectedText)) return;

            // Add separator + "Create Command" item
            var sep2 = new Separator { Tag = "create-command-sep" };
            var item = new MenuItem
            {
                Header = "Create Command from Selection",
                Tag    = "create-command"
            };
            item.Click += (_, _) =>
            {
                var mainVm = (Window.GetWindow(this) as global::PowerTerminal.MainWindow)?.Vm;
                mainVm?.OpenCommandsFromSelection(selectedText.Trim());
            };
            menu.Items.Add(sep2);
            menu.Items.Add(item);
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
                FocusActiveView();
        }

        /// <summary>
        /// Focuses whichever content is currently visible in the tab:
        /// the PowerEditor (if open) or the Terminal.
        /// </summary>
        private void FocusActiveView()
        {
            if (PowerEditor.Visibility == Visibility.Visible)
                PowerEditor.FocusEditor();
            else
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
