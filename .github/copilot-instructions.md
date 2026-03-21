# PowerTerminal — Copilot Instructions

## What this project is

A Windows WPF terminal application (.NET 10, `net10.0-windows`) combining:
- **Left panel**: SSH terminal tabs with a full VT100/xterm emulator
- **Right panel**: AI chat (OpenAI-compatible API) and a personal Linux command wiki

The solution file is `PowerTerminal.sln` at the repo root. The `.slnx` alternative requires VS 2022 17.12+ preview.

---

## Build & Run

```powershell
# Restore
dotnet restore PowerTerminal.sln

# Build (Debug / Release)
dotnet build PowerTerminal.sln
dotnet build PowerTerminal.sln -c Release

# Run
dotnet run --project src/PowerTerminal/PowerTerminal.csproj

# Publish (framework-dependent)
dotnet publish src/PowerTerminal/PowerTerminal.csproj -c Release -r win-x64 -o ./publish

# Publish (self-contained)
dotnet publish src/PowerTerminal/PowerTerminal.csproj -c Release -r win-x64 --self-contained true -o ./publish-standalone
```

## Tests

Tests use **xUnit** and live in `tests/PowerTerminal.Tests/`.

```powershell
# Run all tests
dotnet test PowerTerminal.sln

# Run a single test by name
dotnet test tests/PowerTerminal.Tests/PowerTerminal.Tests.csproj --filter "FullyQualifiedName~WriteChar_PlacesCharacterAtCursor"
```

The test project references the main project directly (via `ProjectReference`). Internal types are accessible because `PowerTerminal.csproj` declares `<InternalsVisibleTo Include="PowerTerminal.Tests" />`.

---

## Architecture

### MVVM pattern

All ViewModels inherit `ViewModelBase` (`ViewModels/ViewModelBase.cs`), which provides:
- `INotifyPropertyChanged` with `OnPropertyChanged([CallerMemberName])` 
- `Set<T>(ref field, value)` — sets the field and raises change notification only if the value changed
- `RelayCommand` — an `ICommand` implementation (same file) supporting parameterised and parameterless variants; hooks into `CommandManager.RequerySuggested` for `CanExecuteChanged`

Views are in `Views/`, ViewModels in `ViewModels/`, and are wired up via XAML `DataContext` bindings — no DI container.

### Terminal emulator (the core)

The terminal emulator is split into two classes in `Controls/`:

- **`TerminalBuffer`** — a 2D character cell array (rows × cols). Owns cursor position, scrolling regions, scrollback (5,000 lines), alternate screen buffer, SGR attribute tracking, and all VT/ANSI escape sequence logic. Has no WPF dependency — tested directly in `TerminalBufferTests`.
- **`TerminalControl`** — a WPF control that extends AvalonEdit's `TextEditor`. It owns a `TerminalBuffer`, a `ConcurrentQueue` of incoming data, and a batch render loop on a background thread. It translates key/mouse events to VT sequences and fires a `UserInput` event for the ViewModel to forward to SSH.

`TerminalControl` exposes:
- `UserInput` event — raw VT bytes to send to the SSH shell
- `TitleChanged` event — OSC title changes
- `BellRung` event — BEL character received
- `TerminalResized` event — buffer dimensions changed (triggers PTY resize via `SshService.Resize`)

### Services

Plain C# classes (no interface/DI), instantiated directly by ViewModels:

| Service | Responsibility |
|---|---|
| `SshService` | SSH connection + shell stream (SSH.NET). Exposes `Resize(cols, rows)` for PTY resize. |
| `AiService` | HTTP client for OpenAI-compatible APIs (streaming) |
| `ConfigService` | Read/write JSON config files under `config/` next to the executable |
| `WikiService` | CRUD + full-text search over `config/wikis/*.json` |
| `LoggingService` | Timestamped daily log files in `logs/` (terminal, AI, wiki) |

### Config & data files

All runtime config is JSON, in `config/` next to the binary (copied from `src/PowerTerminal/config/` via `<CopyToOutputDirectory>PreserveNewest`):
- `config/connections.json` — SSH connections
- `config/settings.json` — AI + theme settings
- `config/wikis/*.json` — individual wiki entries

### Theming

All WPF styles and colours are in `Themes/DarkTheme.xaml`, merged into `App.xaml`. Colour keys are defined there and referenced throughout XAML. Do not hardcode colours in individual XAML views.

---

## Key conventions

### Nullable
- Main project (`PowerTerminal.csproj`): `<Nullable>disable</Nullable>` — nullability annotations are optional/used selectively.
- Test project: `<Nullable>enable</Nullable>` — nullable warnings are enforced.

### No global using-style suppression for nulls in tests
Tests are stricter; use `!` (null-forgiving) only when you know the value is non-null from context.

### Terminal buffer tests
`TerminalBufferTests` creates a `TerminalBuffer` directly (no WPF control). New terminal escape sequence tests should follow this pattern — test via `TerminalBuffer` methods, not `TerminalControl`.

### Wiki variables
Wiki entry content uses `$VarName$` syntax for predefined machine variables and `$variable:PromptLabel$` for user-prompted variables. Variable substitution is handled at the ViewModel layer before sending to the terminal.

### Clitest directory
`../Clitest/` is a separate, independent proof-of-concept project (different solution, .NET 9). It is not part of `PowerTerminal.sln` and is not tested or built as part of this project.

### AvalonEdit usage
`TerminalControl` inherits from `ICSharpCode.AvalonEdit.TextEditor`. The document is rewritten on each render batch by the `AnsiColorizer` (a custom `DocumentColorizingTransformer`). Do not manipulate the AvalonEdit document directly from outside `TerminalControl`.
