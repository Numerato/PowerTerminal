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
- **.NET 8 SDK** – [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Cascadia Code** font (optional but recommended) – included with Windows Terminal

---

## Opening in Visual Studio 2022

### Prerequisites
1. Install **Visual Studio 2022** (version 17.x) — [Download](https://visualstudio.microsoft.com/downloads/)
2. During installation (or via the Visual Studio Installer → **Modify**), enable the workload:
   - ✅ **.NET desktop development**
   
   This workload includes the WPF designer, C# compiler, and all required SDK components.

3. Ensure **.NET 8 SDK** is installed — the **.NET desktop development** workload typically installs it automatically. If needed, you can verify or add it manually under **Individual components → .NET SDK 8.x** in the Visual Studio Installer.

### Opening the Solution
1. Clone the repository:
   ```
   git clone https://github.com/Numerato/PowerTerminal.git
   ```
2. Open Visual Studio 2022.
3. Choose **File → Open → Project/Solution…**
4. Browse to the repository root and select **`PowerTerminal.sln`**
   > ⚠️ Use **`PowerTerminal.sln`** (not `PowerTerminal.slnx`). The `.slnx` format requires VS 2022 17.12+ preview; the `.sln` works with all VS 2022 versions.

5. Visual Studio will restore NuGet packages automatically. If it doesn't, right-click the solution in **Solution Explorer** and choose **Restore NuGet Packages**.

### Running and Debugging
- Press **F5** to build and run with the debugger attached.
- Press **Ctrl+F5** to run without the debugger.
- The startup project is `PowerTerminal` — it should be **bold** in Solution Explorer. If not, right-click it and choose **Set as Startup Project**.
- Set breakpoints by clicking in the gutter (grey bar on the left of each line). The debugger will pause there.
- The **XAML Hot Reload** panel lets you edit XAML while the app is running.

### Troubleshooting Visual Studio
| Problem | Fix |
|---------|-----|
| "The project doesn't know how to run the profile…" | Right-click project → **Properties** → **Debug** → ensure the profile uses **Project** as launch type |
| NuGet packages not restoring | **Tools → NuGet Package Manager → Package Manager Console** → `Update-Package -reinstall` |
| WPF designer shows blank | Ensure **.NET desktop development** workload is installed; try **Build → Rebuild Solution** |
| `net8.0-windows` target error | Install **.NET 8 SDK** from https://dotnet.microsoft.com/download |

---

## Opening in JetBrains Rider

### Prerequisites
1. Install **JetBrains Rider** (2023.3 or newer recommended) — [Download](https://www.jetbrains.com/rider/)
2. Rider ships with its own .NET SDK management; no separate installation needed. However, having the **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** installed system-wide is recommended.
3. On Windows, WPF support is built in — no additional plugins required.

### Opening the Solution
1. Clone the repository (if you haven't already):
   ```
   git clone https://github.com/Numerato/PowerTerminal.git
   ```
2. Launch Rider.
3. On the **Welcome** screen click **Open** (or **File → Open…**).
4. Browse to the repository root and select either:
   - **`PowerTerminal.sln`** — works with all Rider versions, recommended
   - **`PowerTerminal.slnx`** — works with Rider 2024.1+
5. Rider will index the project and restore NuGet packages automatically.

### Running and Debugging
- Click the **▶ Run** button (top toolbar) or press **Shift+F10** to run.
- Click the **🐛 Debug** button or press **Shift+F9** to run with the debugger.
- The run configuration `PowerTerminal` is created automatically from the `.csproj`.
- Set breakpoints by clicking in the gutter. Rider's debugger supports step-in, step-over, watch windows, and inline variable values.
- The **XAML Preview** panel (View → Tool Windows → XAML Preview) shows a live design-time preview.

### Troubleshooting Rider
| Problem | Fix |
|---------|-----|
| "SDK not found" | **File → Settings → Build, Execution, Deployment → Toolset and Build** — set the .NET CLI path to your installed SDK |
| NuGet packages not restoring | Right-click the solution → **Restore NuGet Packages** |
| XAML preview blank | Rider's XAML preview requires Windows; ensure you are on Windows and the project has built at least once |
| Build error `net8.0-windows` | Install [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and restart Rider |

---

## Build (Command Line)

```powershell
# Clone the repository
git clone https://github.com/Numerato/PowerTerminal.git
cd PowerTerminal

# Restore packages
dotnet restore PowerTerminal.sln

# Build (Debug)
dotnet build PowerTerminal.sln

# Build (Release)
dotnet build PowerTerminal.sln -c Release

# Run directly
dotnet run --project src/PowerTerminal/PowerTerminal.csproj

# Run with explicit launch profile
dotnet run --project src/PowerTerminal/PowerTerminal.csproj --launch-profile "PowerTerminal (Debug)"
dotnet run --project src/PowerTerminal/PowerTerminal.csproj -c Release --launch-profile "PowerTerminal (Release)"

# Publish (Release, framework-dependent — requires .NET 8 on target machine)
dotnet publish src/PowerTerminal/PowerTerminal.csproj -c Release -r win-x64 -o ./publish

# Publish (Release, self-contained — no .NET required on target machine)
dotnet publish src/PowerTerminal/PowerTerminal.csproj -c Release -r win-x64 --self-contained true -o ./publish-standalone
```

The published output in `./publish` or `./publish-standalone` can be run directly on any Windows 10/11 machine.

---

## Quick Start

1. Build and run the application (F5 in VS/Rider, or `dotnet run`)
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

