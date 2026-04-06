# PowerTerminal — Copilot Instructions

## What this project is

A Windows WPF terminal application (.NET 10, `net10.0-windows`) combining:
- **Left panel**: SSH terminal tabs (full VT100/xterm emulator), remote file explorer, embedded remote editor (PowerEdit)
- **Right panel**: AI chat (OpenAI-compatible API) and a personal Linux command wiki

The repository root contains two sub-projects that are built together:
- `powerterminal/` — the main WPF application. Solution file: `powerterminal/PowerTerminal.sln`
- `terminal-control/` — a standalone VT100/SSH library (`Terminal.Control`, `net9.0-windows`) referenced by the main app. Has its own solution: `terminal-control/Terminal.slnx`

---

## Build & Run

All commands run from the **repo root** unless noted:

```powershell
# Restore
dotnet restore powerterminal/PowerTerminal.sln

# Build
dotnet build powerterminal/PowerTerminal.sln
dotnet build powerterminal/PowerTerminal.sln -c Release

# Run
dotnet run --project powerterminal/src/PowerTerminal/PowerTerminal.csproj

# Publish (framework-dependent)
dotnet publish powerterminal/src/PowerTerminal/PowerTerminal.csproj -c Release -r win-x64 -o ./publish

# Publish (self-contained)
dotnet publish powerterminal/src/PowerTerminal/PowerTerminal.csproj -c Release -r win-x64 --self-contained true -o ./publish-standalone
```

## Tests

Two test projects exist. Both use **xUnit**.

```powershell
# Run all tests (main app + terminal library)
dotnet test powerterminal/PowerTerminal.sln

# Run a single test by name (main app tests)
dotnet test powerterminal/tests/PowerTerminal.Tests/PowerTerminal.Tests.csproj --filter "FullyQualifiedName~TestMethodName"

# Run terminal library tests only
dotnet test terminal-control/Terminal.Tests/Terminal.Tests.csproj --filter "FullyQualifiedName~PrintsText"
```

- `powerterminal/tests/PowerTerminal.Tests/` — tests for the WPF app; references `PowerTerminal.csproj` directly. Internal types are accessible via `<InternalsVisibleTo Include="PowerTerminal.Tests" />`.
- `terminal-control/Terminal.Tests/` — tests for `Terminal.Control` (VtParserTests, TerminalEmulatorTests, ScreenBufferTests). No WPF dependency beyond the project target.

---

## Architecture

### Repository structure

```
powerterminal/
  PowerTerminal.sln           ← includes both PowerTerminal and Terminal.Control
  src/PowerTerminal/          ← main WPF app (net10.0-windows)
  tests/PowerTerminal.Tests/  ← xUnit tests for the WPF app

terminal-control/
  Terminal.Control/           ← VT100 library (net9.0-windows, no AvalonEdit)
    Vt/                       ← VtParser, TerminalEmulator, ScreenBuffer, ScreenCell, CharacterAttributes, TerminalColor
    Controls/TerminalControl.cs  ← WPF FrameworkElement (custom GlyphRun rendering)
    Ssh/                      ← ISshTerminalSession, SshTerminalSession (SSH.NET)
  Terminal.Tests/             ← xUnit tests for Terminal.Control
```

### MVVM pattern

All ViewModels inherit `ViewModelBase` (`ViewModels/ViewModelBase.cs`), which provides:
- `INotifyPropertyChanged` with `OnPropertyChanged([CallerMemberName])`
- `Set<T>(ref field, value)` — sets field and raises notification only when value changes
- `RelayCommand` — `ICommand` in the same file; hooks into `CommandManager.RequerySuggested` for `CanExecuteChanged`

Views are in `Views/`, ViewModels in `ViewModels/`, wired via XAML `DataContext` — no DI container.

### Terminal emulator (`terminal-control/`)

The VT100 stack is a three-layer pipeline, all in the `Terminal.Vt` namespace:

| Class | Role |
|---|---|
| `VtParser` | Stateful byte-level parser — produces structured `IVtParserActions` callbacks |
| `TerminalEmulator` | Implements `IVtParserActions`; owns a `ScreenBuffer` and interprets all escape sequences |
| `ScreenBuffer` | 2D `ScreenCell[]` grid (rows × cols), cursor state, scroll region, scrollback (10,000 lines), alternate buffer |

`TerminalControl` (`Terminal.Controls` namespace) is a `FrameworkElement` that uses custom GlyphRun rendering (no AvalonEdit). It wires a `TerminalEmulator` to an `ISshTerminalSession` and exposes:
- `UserInput` event — VT bytes from keyboard/mouse to send to the shell
- `TitleChanged`, `BellRaised`, `CursorVisibilityChanged` events

Terminal library tests instantiate `TerminalEmulator` or `ScreenBuffer` directly — no WPF control needed.

### Services (`powerterminal/src/PowerTerminal/Services/`)

Plain C# classes, instantiated directly by ViewModels (no DI):

| Service | Responsibility |
|---|---|
| `SshService` | SSH password auth + key auth via SSH.NET; delegates session I/O to `ISshTerminalSession` |
| `AiService` | Streaming HTTP client for OpenAI-compatible APIs |
| `ConfigService` | Read/write JSON config files in `config/` next to the executable |
| `WikiService` | CRUD + full-text search over `config/wikis/*.json` |
| `LoggingService` | Timestamped daily log files in `logs/` (terminal, AI, wiki) |
| `FileIconService` | Maps remote file extensions to icon images for the Explorer panel |
| `SyntaxValidationService` | Validates file syntax (bash, YAML, TOML, etc.) used in PowerEdit |

### PowerEdit (remote file editor)

`PowerEditViewModel` / `PowerEditView` is an embedded text editor (AvalonEdit) that can open, edit, and save files on the remote SSH server. It supports:
- Syntax highlighting for bash, yaml, toml, ini, dockerfile, makefile, properties, env (via embedded `.xshd` resources)
- Optional sudo elevation (prompts for password via `SudoPasswordWindow`)
- Find/Replace, Go To Line, font picker

SSH file I/O is injected as `Func<>` delegates from `TerminalTabView.xaml.cs` after construction.

### Remote Explorer

`RemoteExplorerViewModel` / `ExplorerView` is a file browser for the active SSH connection. It raises `OpenFileRequested` events that `TerminalTabViewModel` forwards to PowerEdit.

### Config & data files

All runtime config is JSON, in `config/` next to the binary (copied via `<CopyToOutputDirectory>PreserveNewest`):
- `config/connections.json` — SSH connections
- `config/settings.json` — AI + theme settings
- `config/wikis/*.json` — individual wiki entries

### Theming

All WPF styles and colours are in `Themes/DarkTheme.xaml`, merged into `App.xaml`. Converter resource keys defined there: `BoolToVisConverter`, `BoolToInvisConverter`, `NullOrEmptyToVisInvertConverter`, `NullOrEmptyToVisConverter`, `NullToVisConverter`, `NullToVisInvertConverter`. For input backgrounds use `SurfaceBrush` — there is no `InputBrush`.

Do not hardcode colours in individual XAML views.

---

## Working conventions

- **Always rebuild** after any code change: `dotnet build powerterminal/PowerTerminal.sln -p:EnableWindowsTargeting=true -nologo -v:m`
- **Windows-only** — no cross-platform concerns; WPF and Windows APIs are always available
- **All dialogs must be dark-themed** — never use default WPF `MessageBox` or unstyled windows. Reuse existing dark dialog classes: `DarkMessageBox`, `DarkConfirmWindow`, `DarkChoiceWindow`, `DarkPasswordWindow`, `SudoPasswordWindow`. New dialogs must apply `DarkTheme.xaml` styles.
- **Stubborn bugs** — if a bug takes more than 2–3 attempts to fix, delegate it to a background agent tasked with writing regression tests for it, then continue other work.

---

## Key conventions

### Nullable
- `powerterminal/src/PowerTerminal/` (`PowerTerminal.csproj`): `<Nullable>disable</Nullable>` — annotations used selectively.
- Both test projects: `<Nullable>enable</Nullable>` — warnings enforced. Use `!` (null-forgiving) only when value is known non-null from context.

### Terminal library tests
Instantiate `TerminalEmulator(cols, rows)` directly and call `ProcessBytes()`. Never use `TerminalControl` in tests — it has WPF dependencies.

### Wiki variables
Wiki content uses `$VarName$` for predefined machine variables (e.g., `$hostname$`, `$ipaddress$`) and `$variable:PromptLabel$` for user-prompted variables. Substitution is done in the ViewModel before sending to the terminal.

### `terminal-control/` is its own project
It targets `net9.0-windows` and has no dependency on the main app. The main app references it via `ProjectReference`. Changes to `Terminal.Control` affect both the library tests and the main app. Build `powerterminal/PowerTerminal.sln` to compile both together.
