# Terminal Emulation Status

A detailed summary of what terminal functions are implemented, explicitly ignored,
and still missing in PowerTerminal's VT100/xterm emulator.

> **Last updated:** March 2026 — based on `TerminalControl.cs` and `TerminalBuffer.cs`

---

## ✅ What Works

### Keyboard Input
| Key / Combo | Sequence Sent | Status |
|---|---|---|
| Printable characters | literal | ✅ |
| Enter | `\r` | ✅ |
| Backspace | `\x7F` (DEL) | ✅ |
| Tab | `\t` | ✅ |
| Shift+Tab | `ESC[Z` (reverse tab) | ✅ |
| Escape | `\x1B` | ✅ |
| Arrow keys (normal) | `ESC[A/B/C/D` | ✅ |
| Arrow keys (app mode) | `ESC O A/B/C/D` | ✅ |
| Home / End | `ESC[H` / `ESC[F` | ✅ |
| Insert | `ESC[2~` | ✅ |
| Delete | `ESC[3~` | ✅ |
| Page Up / Page Down | `ESC[5~` / `ESC[6~` | ✅ |
| F1–F4 | `ESC O P/Q/R/S` | ✅ |
| F5–F12 | `ESC[15~` … `ESC[24~` | ✅ |
| Ctrl+A–Z | `\x01`–`\x1A` | ✅ |
| Ctrl+C (with selection) | Clipboard copy | ✅ |
| Ctrl+V / Shift+Insert | Paste (bracketed if enabled) | ✅ |
| Space | literal space | ✅ |

### CSI Sequences (ESC [ …)
| Sequence | Name | Status |
|---|---|---|
| `CSI n A` | CUU – Cursor Up | ✅ |
| `CSI n B` | CUD – Cursor Down | ✅ |
| `CSI n C` | CUF – Cursor Forward | ✅ |
| `CSI n D` | CUB – Cursor Backward | ✅ |
| `CSI n E` | CNL – Cursor Next Line | ✅ |
| `CSI n F` | CPL – Cursor Previous Line | ✅ |
| `CSI n G` | CHA – Cursor Horizontal Absolute | ✅ |
| `CSI r;c H` | CUP – Cursor Position | ✅ |
| `CSI r;c f` | HVP – Horizontal & Vertical Position | ✅ |
| `CSI n J` | ED – Erase in Display (0, 1, 2, 3) | ✅ |
| `CSI n K` | EL – Erase in Line (0, 1, 2) | ✅ |
| `CSI n L` | IL – Insert Lines | ✅ |
| `CSI n M` | DL – Delete Lines | ✅ |
| `CSI n P` | DCH – Delete Characters | ✅ |
| `CSI n S` | SU – Scroll Up | ✅ |
| `CSI n T` | SD – Scroll Down | ✅ |
| `CSI n X` | ECH – Erase Characters | ✅ |
| `CSI n @` | ICH – Insert Characters | ✅ |
| `CSI n d` | VPA – Vertical Position Absolute | ✅ |
| `CSI t;b r` | DECSTBM – Set Scrolling Region | ✅ |
| `CSI s` | SCP – Save Cursor Position | ✅ |
| `CSI u` | RCP – Restore Cursor Position | ✅ |
| `CSI 6 n` | DSR – Device Status Report (cursor position) | ✅ |
| `CSI c` | DA – Device Attributes (responds VT100) | ✅ |
| `CSI … m` | SGR – Select Graphic Rendition | ✅ |

### SGR Attributes (CSI … m)
| Code | Attribute | Status |
|---|---|---|
| 0 | Reset all | ✅ |
| 1 | Bold | ✅ |
| 2 | Dim / faint | ✅ |
| 3 | Italic | ✅ |
| 4 | Underline | ✅ |
| 7 | Inverse / reverse video | ✅ |
| 9 | Strikethrough | ✅ |
| 21 | Double underline (rendered as underline) | ✅ |
| 22 | Normal intensity (reset bold + dim) | ✅ |
| 23 | Not italic | ✅ |
| 24 | Not underlined | ✅ |
| 27 | Not inverse | ✅ |
| 29 | Not strikethrough | ✅ |
| 30–37 | Foreground (standard 8 colours) | ✅ |
| 38;5;n | Foreground (256-colour palette) | ✅ |
| 38;2;r;g;b | Foreground (24-bit TrueColor) | ✅ |
| 39 | Default foreground | ✅ |
| 40–47 | Background (standard 8 colours) | ✅ |
| 48;5;n | Background (256-colour palette) | ✅ |
| 48;2;r;g;b | Background (24-bit TrueColor) | ✅ |
| 49 | Default background | ✅ |
| 90–97 | Foreground (bright / high-intensity) | ✅ |
| 100–107 | Background (bright / high-intensity) | ✅ |

### ESC Sequences
| Sequence | Name | Status |
|---|---|---|
| `ESC D` | IND – Index (line feed with scroll) | ✅ |
| `ESC M` | RI – Reverse Index (scroll down) | ✅ |
| `ESC E` | NEL – Next Line | ✅ |
| `ESC 7` | DECSC – Save Cursor | ✅ |
| `ESC 8` | DECRC – Restore Cursor | ✅ |
| `ESC c` | RIS – Full Reset | ✅ |

### DEC Private Modes (CSI ? … h/l)
| Mode | Name | Status |
|---|---|---|
| `?1` | Application Cursor Keys (DECCKM) | ✅ |
| `?7` | Auto-Wrap Mode (DECAWM) | ✅ |
| `?25` | Cursor Visible (DECTCEM) | ✅ (tracked in buffer) |
| `?47` | Alternate Screen Buffer (old) | ✅ |
| `?1047` | Alternate Screen Buffer | ✅ |
| `?1048` | Save / Restore Cursor | ✅ |
| `?1049` | Save Cursor + Alternate Buffer (vim/nano/htop) | ✅ |
| `?2004` | Bracketed Paste Mode | ✅ |

### Control Characters
| Char | Name | Status |
|---|---|---|
| `\n` (0x0A) | Line Feed | ✅ |
| `\r` (0x0D) | Carriage Return | ✅ |
| `\x08` | Backspace | ✅ |
| `\t` (0x09) | Horizontal Tab | ✅ (fixed 8-column stops) |
| `\x07` | Bell | ✅ (silent / no-op) |
| `\x0B` | Vertical Tab (treated as LF) | ✅ |
| `\x0C` | Form Feed (treated as LF) | ✅ |

### Other Features
| Feature | Status |
|---|---|
| 2D character cell buffer | ✅ |
| Scrollback (5 000 lines, primary buffer only) | ✅ |
| Alternate screen buffer (with save/restore) | ✅ |
| Scrolling regions (DECSTBM) | ✅ |
| Deferred auto-wrap (xterm-style) | ✅ |
| Concurrent queue + batch rendering for performance | ✅ |
| Character-by-character escape parser (handles chunk splits) | ✅ |
| Hidden-input mode (password prompts) | ✅ |
| PTY resize (`SshService.Resize`) | ✅ |
| Copy & Paste context menu | ✅ |
| OSC sequence parsing (consumed & discarded) | ✅ |
| Character set designation (consumed & discarded) | ✅ |

---

## ⚠️ Explicitly Ignored (code has comments noting intentional skip)

These sequences are parsed but intentionally produce no effect:

| Sequence | Name | Reason |
|---|---|---|
| `ESC H` | HTS – Set Tab Stop | Tab stops are hardcoded every 8 columns |
| `ESC =` | DECKPAM – Application Keypad Mode | Numeric keypad not relevant for SSH terminal |
| `ESC >` | DECKPNM – Numeric Keypad Mode | Same as above |
| `?12` | Blinking cursor (DECSET/DECRST) | WPF caret doesn't support blink toggle |
| `CSI b` | REP – Repeat preceding character | Rarely used; ignored for simplicity |
| OSC sequences | Window title, colour changes, etc. | Parsed and discarded (no window title update) |
| CSI `>` / `!` prefix | Secondary DA, soft reset, etc. | Private prefix silently ignored |
| `ESC ( X` / `ESC ) X` | Character set designation (G0/G1) | Not needed for UTF-8 terminals |

---

## ❌ Not Yet Implemented

### High Impact (affects usability of terminal programs)

| Feature | Impact | Details |
|---|---|---|
| **Dynamic terminal resize** | **High** | PTY is created with fixed 220×50. The TerminalBuffer has a `Resize()` method but it is **never called** when the WPF window resizes. vim/nano/htop show wrong dimensions. Must measure character cell size, compute rows/cols from control size, call `buffer.Resize()` + `ssh.Resize()`, and send `SIGWINCH`. |
| **Mouse reporting** | **High** | Modes `?9`, `?1000`, `?1002`, `?1003`, `?1006`, `?1015` are not handled. htop, mc (Midnight Commander), and tmux mouse interactions don't work. |
| **`CursorVisible` not rendered** | **Medium** | `?25` is tracked in the buffer (`CursorVisible` flag) but the WPF caret is never hidden/shown based on this flag. Programs that hide the cursor (nano status bar, htop) still show a blinking caret. |
| **SGR 5 – Blink** | **Low** | Blink attribute (SGR 5) is not parsed. Some programs use it for emphasis. Could map to bold or ignore gracefully. |
| **SGR 8 – Hidden/Invisible text** | **Low** | Not parsed. Used rarely (password fields rendered server-side). |
| **Underline colour (SGR 58;5;n / 58;2;r;g;b)** | **Low** | Underline is rendered but always in the foreground colour. Kitty/modern terminals support separate underline colours. |

### Medium Impact (edge cases, less common programs)

| Feature | Impact | Details |
|---|---|---|
| **Custom tab stops (HTS / TBC)** | **Medium** | `ESC H` (set tab) and `CSI g` / `CSI 3 g` (clear tabs) are ignored. Fixed 8-column stops work for most programs, but some (e.g. `tabs` command, column formatters) set custom stops. |
| **Cursor shape (DECSCUSR)** | **Medium** | `CSI n SP q` for block/underline/bar cursor is not handled. vim, zsh, fish change cursor shape by mode. |
| **OSC window title** | **Medium** | `ESC ] 0;title BEL` is parsed and discarded. Could set the tab header to the shell's working directory or running program name. |
| **Origin Mode (DECOM, `?6`)** | **Medium** | Cursor positioning relative to scroll region. Some full-screen programs rely on this for status bars within regions. |
| **CSI `$` sequences** | **Low** | Selective erase `DECSED`/`DECSEL` — rare but used by some screen drawing tools. |
| **Insert/Replace Mode (IRM, `?4`)** | **Low** | `CSI 4 h` / `CSI 4 l` toggle insert mode for typed characters. Default replace mode is correct for most use. |
| **REP (CSI b) – Repeat character** | **Low** | Some programs optimise drawing by repeating the last character N times instead of sending N copies. Currently ignored. |

### Low Impact (rare, specialised, or cosmetic)

| Feature | Impact | Details |
|---|---|---|
| **Sixel / iTerm2 inline graphics** | **Low** | Image protocols used by `img2sixel`, `chafa`, `viu`. Very complex; not needed for SSH admin use. |
| **Unicode combining characters / wide chars** | **Low** | CJK double-width characters and combining marks (e.g. accented characters composed from multiple code points) may mis-align. The buffer stores one `char` per cell without width awareness. |
| **Synchronized output (Mode 2026)** | **Low** | `CSI ? 2026 h/l` — allows batch screen updates to reduce flicker. Not critical but nice for fast-scrolling output. |
| **Focus reporting (`?1004`)** | **Low** | Terminal sends `ESC [I` / `ESC [O` when window gains/loses focus. vim can detect focus changes. |
| **Modified key reporting** | **Low** | `CSI 27;mod;key ~` or xterm modified-key sequences for Ctrl+Shift+key, Alt+Shift+key combos. Only Ctrl+letter, Shift+Tab, and basic modifiers are currently mapped. |
| **Soft reset (CSI ! p)** | **Low** | DECSTR — partial reset without clearing screen. Prefix `!` is currently silently ignored. |
| **Secondary DA (CSI > c)** | **Low** | Reports terminal type/version. Prefix `>` is silently ignored. |
| **DECALN (ESC # 8)** | **Low** | Fill screen with `E` characters for alignment test. Not used in practice. |
| **Save/restore DEC private modes** | **Low** | `CSI ? … s` / `CSI ? … r` — save and restore individual mode flags. tmux uses this. |
| **X11 colour queries (OSC 10/11/12)** | **Low** | Programs query foreground/background colours via OSC. Parsed and discarded; no response sent. |
| **Numpad application mode** | **Low** | `ESC =` / `ESC >` are parsed but ignored. Numpad keys send standard sequences regardless. |
| **Bell – visual or audio** | **Low** | Bell (`\x07`) is a no-op. Could flash the tab header or play a system sound. |

---

## Summary Counts

| Category | Count |
|---|---|
| ✅ Implemented and working | ~65 features/sequences |
| ⚠️ Explicitly ignored (by design) | 8 |
| ❌ Missing – High impact | 3 (resize, mouse, cursor visibility) |
| ❌ Missing – Medium impact | 5 |
| ❌ Missing – Low impact | 12+ |

---

## Recommended Next Steps (priority order)

1. **Dynamic resize** — measure terminal control size on load and resize events, update buffer + PTY dimensions. This is the single biggest gap: without it vim/nano display wrong column counts.
2. **Cursor visibility** — wire `_buffer.CursorVisible` to `TextArea.Caret.CaretBrush` (transparent when hidden).
3. **Mouse reporting** — implement `?1000`/`?1002`/`?1006` so htop, mc, and tmux mouse clicks work.
4. **OSC 0 window title** — display the server-set title in the tab header.
5. **Cursor shape** — change caret style for block/beam/underline per `CSI n SP q`.
