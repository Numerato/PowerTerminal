# ⚡ PowerTerminal

A Windows WPF terminal application combining SSH terminal sessions, AI chat, and a personal Linux command wiki — all in one dark-themed split-screen interface inspired by Windows Terminal Canary and Warp.

---

## Features

### Left Panel – SSH Terminal Tabs
- **Multiple SSH tabs** – open connections to multiple Linux machines simultaneously
- **Full VT100/ANSI colour support** – 16 standard colours, 256-colour palette, RGB (24-bit) colours
- **Dark terminal** – matching the Windows Terminal Canary / Warp aesthetic
- **Auto-connects** on startup using saved connections
- **Machine info gathering** – on connection, collects: hostname, OS, kernel version, home folder, CPU, memory, disk sizes, IP address, uptime

### Right Panel – AI Chat & Wiki

#### AI Chat Tab
- Chat with any **OpenAI-compatible AI API** (OpenAI, Azure OpenAI, local LLM servers, etc.)
- Configurable: API endpoint, token, model, temperature, system prompt
- Full conversation history with copy/cancel support
- Interactions logged to `logs/ai_YYYY-MM-DD.log`

#### Wiki Tab
- **Personal knowledge base** – store Linux commands and procedures as wiki entries
- **Full-text search** across title, description, tags, and content
- **Markdown-style content** – text sections and command blocks
- **Command blocks** have two action buttons:
  - **Copy** – pastes command into the active terminal (no Enter)
  - **Copy & Execute** – pastes command and presses Enter
- **Variable substitution**:
  - Predefined machine variables: `$CurrentDirectory$`, `$OperatingSystem$`, `$version$`, `$homefolder$`, `$hardware$`, `$disksizes$`, `$ipaddress$`, `$hostname$`, `$cpu$`, `$memory$`, `$username$`, `$uptime$`, `$kernelversion$`
  - User prompt variables: `$variable:Name$` – shows a dialog prompting for the value
- **CRUD editor** – create, edit, and delete wiki entries with a multi-section editor

### Configuration
- All config stored as JSON in `config/` folder (next to the executable)
- `config/connections.json` – SSH connections
- `config/settings.json` – AI settings and theme settings
- `config/wikis/*.json` – individual wiki entry files

### Logging
Three separate log files in the `logs/` folder, each with timestamps:
- `logs/terminal_YYYY-MM-DD.log` – SSH input/output and connection events
- `logs/ai_YYYY-MM-DD.log` – AI messages and errors
- `logs/wiki_YYYY-MM-DD.log` – wiki searches, copies, and executions

---

## Demo Data

The app ships with:

**Connection:**
- `dockerhome` → `Geertm@dockerhome.local:22`

**Wiki entries (7):**
1. **Managing Folders on Linux** – mkdir, ls, cd, cp, mv, rm with variable prompts
2. **Install Docker on Linux** – complete Docker Engine installation for Ubuntu/Debian
3. **Install Samba (SMB) on Linux** – share folders from Linux to Windows
4. **Set a Static IP Address** – Netplan configuration with IP/subnet/gateway/DNS variables
5. **Change the Hostname** – hostnamectl + /etc/hosts update with variable prompt
6. **Run .NET App as Ubuntu Daemon** – systemd service setup with variable prompts
7. **Must-Have Ubuntu Tools** – nano, htop, btop, ncdu, tmux, nmap, and more

---

## Requirements

- **Windows 10/11** (WPF, Windows-only)
- **.NET 8 Runtime** – [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Cascadia Code** font (optional but recommended) – included with Windows Terminal

---

## Build

```powershell
# Clone the repository
git clone https://github.com/Numerato/PowerTerminal.git
cd PowerTerminal

# Restore packages
dotnet restore src/PowerTerminal/PowerTerminal.csproj

# Build (Debug)
dotnet build src/PowerTerminal/PowerTerminal.csproj

# Publish (Release, self-contained)
dotnet publish src/PowerTerminal/PowerTerminal.csproj -c Release -r win-x64 --self-contained true -o ./publish
```

The published output in `./publish` can be run directly on any Windows 10/11 machine.

---

## Quick Start

1. Build and run the application
2. Click **Connections** to manage SSH connections
3. Add your server details (host, username, port, password or private key path)
4. Click **Connect** – a new terminal tab opens and connects automatically
5. Use the **Wiki** tab to search your command library
6. Click **Copy** or **Copy & Execute** on any command block to run it in the active terminal
7. Configure AI by clicking **Settings** and entering your API token and model

---

## Architecture

```
src/PowerTerminal/
├── App.xaml                    # Application entry point + global exception handling
├── MainWindow.xaml             # Main split-screen window
├── Controls/
│   └── TerminalControl.cs      # VT100/ANSI terminal emulator (WPF RichTextBox based)
├── Converters/
│   └── Converters.cs           # All IValueConverter implementations
├── Models/
│   ├── AiMessage.cs            # Chat message model
│   ├── AppSettings.cs          # Application + AI + theme settings
│   ├── MachineInfo.cs          # Gathered machine information
│   ├── SshConnection.cs        # SSH connection configuration
│   └── WikiEntry.cs            # Wiki entry + section models
├── Services/
│   ├── AiService.cs            # OpenAI-compatible HTTP client
│   ├── ConfigService.cs        # JSON config file read/write
│   ├── LoggingService.cs       # Timestamped log files (terminal, AI, wiki)
│   ├── SshService.cs           # SSH connection + shell stream (SSH.NET)
│   └── WikiService.cs          # Wiki search and CRUD
├── Themes/
│   └── DarkTheme.xaml          # Dark colour palette + all control styles
├── ViewModels/
│   ├── AiChatViewModel.cs
│   ├── ConnectionManagerViewModel.cs
│   ├── MainViewModel.cs
│   ├── TerminalTabViewModel.cs
│   ├── ViewModelBase.cs        # INotifyPropertyChanged + RelayCommand
│   ├── WikiEditorViewModel.cs
│   └── WikiViewModel.cs
├── Views/
│   ├── AiChatView.xaml(.cs)
│   ├── ConnectionManagerWindow.xaml(.cs)
│   ├── SettingsWindow.xaml(.cs)
│   ├── TerminalTabView.xaml(.cs)
│   ├── VariablePromptWindow.xaml(.cs)
│   ├── WikiEditorWindow.xaml(.cs)
│   └── WikiView.xaml(.cs)
└── config/                     # Demo config files (copied to output)
    ├── connections.json
    ├── settings.json
    └── wikis/
        ├── linux_folders.json
        ├── install_docker.json
        ├── install_smb.json
        ├── static_ip.json
        ├── change_hostname.json
        ├── dotnet_daemon.json
        └── ubuntu_must_have_tools.json
```

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [SSH.NET](https://github.com/sshnet/SSH.NET) | 2024.2.0 | SSH connections and shell streams |
| [Markdig](https://github.com/xoofx/markdig) | 0.40.0 | Markdown parsing (for wiki text sections) |
| Microsoft.Extensions.Logging | 8.0.1 | Logging abstractions |

