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
| `CSI n b` | REP – Repeat preceding character | ✅ |
| `CSI n g` | TBC – Tab Clear (0=current, 3=all) | ✅ |
| `CSI 4 h` | SM – Set Insert Mode (IRM) | ✅ |
| `CSI 4 l` | RM – Reset Insert Mode | ✅ |
| `CSI > c` | Secondary DA – Terminal identification | ✅ |
| `CSI ! p` | DECSTR – Soft Reset | ✅ |
| `CSI n SP q` | DECSCUSR – Set Cursor Shape | ✅ |
| `CSI n $ J` | DECSED – Selective Erase in Display | ✅ |
| `CSI n $ K` | DECSEL – Selective Erase in Line | ✅ |

### SGR Attributes (CSI … m)
| Code | Attribute | Status |
|---|---|---|
| 0 | Reset all | ✅ |
| 1 | Bold | ✅ |
| 2 | Dim / faint | ✅ |
| 3 | Italic | ✅ |
| 4 | Underline | ✅ |
| 5 | Slow blink (mapped to bold for visual effect) | ✅ |
| 6 | Rapid blink (mapped to bold for visual effect) | ✅ |
| 7 | Inverse / reverse video | ✅ |
| 8 | Hidden / invisible (fg set to bg) | ✅ |
| 9 | Strikethrough | ✅ |
| 21 | Double underline (rendered as underline) | ✅ |
| 22 | Normal intensity (reset bold + dim) | ✅ |
| 23 | Not italic | ✅ |
| 24 | Not underlined | ✅ |
| 25 | Not blink | ✅ |
| 27 | Not inverse | ✅ |
| 28 | Not hidden | ✅ |
| 29 | Not strikethrough | ✅ |
| 30–37 | Foreground (standard 8 colours) | ✅ |
| 38;5;n | Foreground (256-colour palette) | ✅ |
| 38;2;r;g;b | Foreground (24-bit TrueColor) | ✅ |
| 39 | Default foreground | ✅ |
| 40–47 | Background (standard 8 colours) | ✅ |
| 48;5;n | Background (256-colour palette) | ✅ |
| 48;2;r;g;b | Background (24-bit TrueColor) | ✅ |
| 49 | Default background | ✅ |
| 58;5;n | Underline colour (256-colour palette) | ✅ |
| 58;2;r;g;b | Underline colour (24-bit TrueColor) | ✅ |
| 59 | Default underline colour | ✅ |
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
| `ESC H` | HTS – Set Tab Stop at current column | ✅ |
| `ESC # 8` | DECALN – Screen Alignment Pattern (fill with E) | ✅ |

### DEC Private Modes (CSI ? … h/l)
| Mode | Name | Status |
|---|---|---|
| `?1` | Application Cursor Keys (DECCKM) | ✅ |
| `?6` | Origin Mode (DECOM) | ✅ |
| `?7` | Auto-Wrap Mode (DECAWM) | ✅ |
| `?9` | X10 Mouse Reporting | ✅ |
| `?25` | Cursor Visible (DECTCEM) | ✅ (rendered: caret hidden when off) |
| `?47` | Alternate Screen Buffer (old) | ✅ |
| `?1000` | Normal Mouse Tracking | ✅ |
| `?1002` | Button-Event Mouse Tracking | ✅ |
| `?1003` | Any-Event Mouse Tracking | ✅ |
| `?1004` | Focus Reporting (sends ESC[I / ESC[O) | ✅ |
| `?1006` | SGR Mouse Encoding | ✅ |
| `?1015` | URXVT Mouse Encoding | ✅ |
| `?1047` | Alternate Screen Buffer | ✅ |
| `?1048` | Save / Restore Cursor | ✅ |
| `?1049` | Save Cursor + Alternate Buffer (vim/nano/htop) | ✅ |
| `?2004` | Bracketed Paste Mode | ✅ |
| `?2026` | Synchronized Output | ✅ |
| `CSI ? … s` | Save DEC Private Modes | ✅ |
| `CSI ? … r` | Restore DEC Private Modes | ✅ |

### Control Characters
| Char | Name | Status |
|---|---|---|
| `\n` (0x0A) | Line Feed | ✅ |
| `\r` (0x0D) | Carriage Return | ✅ |
| `\x08` | Backspace | ✅ |
| `\t` (0x09) | Horizontal Tab | ✅ (custom + default 8-column stops) |
| `\x07` | Bell | ✅ (system beep via BellRung event) |
| `\x0B` | Vertical Tab (treated as LF) | ✅ |
| `\x0C` | Form Feed (treated as LF) | ✅ |

### OSC Sequences
| Sequence | Name | Status |
|---|---|---|
| `OSC 0;title` | Set Window Title + Icon | ✅ (updates tab header via TitleChanged event) |
| `OSC 1;title` | Set Icon Name | ✅ (updates tab header) |
| `OSC 2;title` | Set Window Title | ✅ (updates tab header) |
| `OSC 10;?` | Query Foreground Colour | ✅ (responds with current colour) |
| `OSC 11;?` | Query Background Colour | ✅ (responds with current colour) |
| `OSC 12;?` | Query Cursor Colour | ✅ (responds with current colour) |

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
| CSI intermediate byte handling (space, $, etc.) | ✅ |
| Hidden-input mode (password prompts) | ✅ |
| PTY resize (`SshService.Resize`) | ✅ |
| **Dynamic terminal resize** (SizeChanged → buffer + PTY) | ✅ |
| Copy & Paste context menu | ✅ |
| OSC sequence parsing with title/colour extraction | ✅ |
| Character set designation (consumed & discarded) | ✅ |
| Mouse reporting (X10/normal/button/any + SGR/URXVT encoding) | ✅ |
| Cursor visibility rendered (caret hidden when `?25` off) | ✅ |
| Cursor shape tracking (DECSCUSR) | ✅ |
| Insert/Replace mode (IRM) | ✅ |
| Origin mode (DECOM) | ✅ |
| Custom tab stops (HTS/TBC) | ✅ |
| Focus reporting (`?1004`) | ✅ |
| Synchronized output mode (`?2026`) | ✅ |
| Visual bell (system beep on BEL character) | ✅ |
| Save/restore DEC private modes (`CSI ? … s/r`) | ✅ |

---

## ⚠️ Explicitly Ignored (by design, with minimal impact)

| Sequence | Name | Reason |
|---|---|---|
| `ESC =` | DECKPAM – Application Keypad Mode | Numeric keypad not relevant for SSH terminal |
| `ESC >` | DECKPNM – Numeric Keypad Mode | Same as above |
| `?12` | Blinking cursor (DECSET/DECRST) | WPF caret blink is OS-controlled |

---

## ❌ Not Yet Implemented (low-impact edge cases)

| Feature | Impact | Details |
|---|---|---|
| **Sixel / iTerm2 inline graphics** | **Low** | Image protocols used by `img2sixel`, `chafa`, `viu`. Very complex; not needed for SSH admin use. |
| **Unicode combining characters / wide chars** | **Low** | CJK double-width characters and combining marks may mis-align. The buffer stores one `char` per cell without width awareness. |
| **Modified key reporting** | **Low** | `CSI 27;mod;key ~` or xterm modified-key sequences for Ctrl+Shift+key, Alt+Shift+key combos. Only Ctrl+letter, Shift+Tab, and basic modifiers are currently mapped. |

---

## Summary Counts

| Category | Count |
|---|---|
| ✅ Implemented and working | ~95+ features/sequences |
| ⚠️ Explicitly ignored (by design) | 3 |
| ❌ Not implemented (low impact only) | 3 |

---

## Recommended Next Steps (if desired)

1. **Unicode wide character support** — Track column widths using Unicode East Asian Width property for CJK characters.
2. **Sixel graphics** — Would require a custom inline image renderer, significant effort for limited use case.
3. **Modified key reporting** — Extend `KeyToSequence` to emit `CSI 27;mod;key ~` format for Ctrl+Shift/Alt+Shift combos.
