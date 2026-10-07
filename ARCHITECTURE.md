# OS-CopyPaster — Architecture Blueprint

## 1. High-Level Summary

OS-CopyPaster is a headless Windows 11 background utility that provides **10 secondary clipboard buffers** (slots 0–9), independent of the native OS clipboard. It intercepts user-defined global hotkeys to perform "Secondary Copy" and "Secondary Paste" operations by temporarily swapping the OS clipboard contents behind the scenes.

**Target Framework:** .NET 8 (Windows Desktop), WinForms (headless — no visible windows)  
**Language:** C# 12

---

## 2. Threading Model — The Single Most Critical Design Decision

This project has three subsystems that each impose strict threading constraints:

| Subsystem | Constraint |
|---|---|
| **Low-Level Keyboard Hook** | Must be installed on a thread with an active **message pump**. The hook callback must return within ~300ms or Windows will silently skip it. |
| **Clipboard Operations** | `System.Windows.Forms.Clipboard` requires an **STA (Single-Threaded Apartment)** thread. |
| **System Tray (NotifyIcon)** | WinForms `NotifyIcon` requires an STA thread with a message pump. |

### Decision: Single STA Thread with WinForms Message Loop

All three subsystems will run on the **main application thread**, which is marked `[STAThread]` and runs `Application.Run(ApplicationContext)`. This gives us:

- ✅ A message pump (required for the keyboard hook)
- ✅ An STA thread (required for clipboard operations)
- ✅ WinForms compatibility (required for NotifyIcon)
- ✅ No cross-thread marshaling needed

```
┌──────────────────────────────────────────────────────┐
│              MAIN THREAD  [STA]                      │
│                                                      │
│  ┌─────────────┐  ┌─────────────┐  ┌──────────────┐ │
│  │ NotifyIcon  │  │  Keyboard   │  │  Clipboard   │ │
│  │ (Tray UI)   │  │    Hook     │  │  Operations  │ │
│  └─────────────┘  └──────┬──────┘  └──────────────┘ │
│                          │                  ▲        │
│                          │  Post via        │        │
│                          │  SyncContext      │        │
│                          └──────────────────┘        │
│                                                      │
│  Application.Run()  ◄── Message Pump                 │
└──────────────────────────────────────────────────────┘
```

### Why Not a Separate Thread for the Hook?

A separate thread would require its own message pump (`Application.Run` or manual `GetMessage`/`DispatchMessage` loop), and then clipboard operations would still need to be marshaled back to an STA thread. This adds complexity with zero benefit. Keeping everything on one thread is simpler and eliminates race conditions.

### How the Hook Callback Stays Fast

The hook callback does exactly three things:
1. Check if the pressed key matches a configured hotkey → if not, `CallNextHookEx` immediately.
2. If it matches, set a flag and **return 1** (suppress the keypress) — this takes <1μs.
3. Post the actual clipboard work to the message queue via `SynchronizationContext.Post()` so it runs **after** the hook callback returns.

This ensures the callback never blocks.

---

## 3. Multi-Buffer Hotkey Design — Two-Key Chord System

### The Problem

With 10 independent clipboard slots, we need a way for the user to select *which* slot to copy into or paste from. Assigning a unique three-modifier keybind to each slot (e.g., `Ctrl+Alt+Shift+1`) is ergonomically terrible and conflicts with many apps.

### The Solution: Two-Key Chord Sequences

The hotkey system uses a **two-key chord** (press one combo, then a digit):

| Action | Chord |
|---|---|
| Copy to slot 3 | `Ctrl+;` then `3` |
| Paste from slot 3 | `Ctrl+'` then `3` |
| Copy to slot 1 (quick) | `Ctrl+;` then `1` *(or just `Ctrl+;` with auto-timeout to slot 1)* |

**Flow:**
1. User presses `Ctrl+;` → hook suppresses `;`, enters **AwaitingCopySlot** state, starts a 1-second timer.
2. User presses `3` → hook suppresses `3`, fires `SecondaryCopy(slot: 3)`, returns to **Idle**.
3. If 1 second passes with no digit → auto-fires `SecondaryCopy(slot: 1)` as default, returns to **Idle**.
4. If user presses a non-digit key → cancels, returns to **Idle**, passes the key through.

Same pattern for `Ctrl+'` → **AwaitingPasteSlot**.

### State Machine

```
                  Ctrl+;              digit (0-9)
        ┌───────────────────┐     ┌─────────────────┐
        │                   ▼     │                 ▼
    ┌───────┐        ┌──────────────┐         Fire Copy(slot)
    │ Idle  │        │ AwaitingCopy │────────► return to Idle
    │       │        │    Slot      │
    └───┬───┘        └──────┬───────┘
        │                   │
        │  timeout (1s)     │ non-digit key
        │  ◄────────────────┤
        │  Fire Copy(1)     │ Cancel, pass key through
        │                   │
        │  Ctrl+'           │
        ├───────────────────┤
        │                   ▼
        │            ┌──────────────┐         Fire Paste(slot)
        │            │ AwaitingPaste│────────► return to Idle
        └────────────│    Slot      │
          timeout    └──────────────┘
          Fire Paste(1)
```

---

## 4. Folder & File Structure

```
OS-CopyPaster/
├── ARCHITECTURE.md              ← This file
├── README.md
│
├── src/
│   └── OsCopyPaster/
│       ├── OsCopyPaster.csproj
│       ├── Program.cs                          # [STAThread] entry point
│       │
│       ├── App/
│       │   ├── TrayApplication.cs              # ApplicationContext subclass, owns NotifyIcon
│       │   └── AppState.cs                     # Toggle On/Off, observable state
│       │
│       ├── Hooks/
│       │   ├── GlobalKeyboardHook.cs           # SetWindowsHookEx + chord state machine
│       │   ├── KeyCombo.cs                     # Value type: modifier flags + virtual key
│       │   └── HotkeyAction.cs                 # Enum/record: Copy/Paste + slot number
│       │
│       ├── Clipboard/
│       │   ├── ClipboardManager.cs             # Save / restore / read / write clipboard
│       │   └── ClipboardSwapOrchestrator.cs    # Full copy/paste swap sequences
│       │
│       ├── Input/
│       │   └── InputSimulator.cs               # SendInput wrapper for Ctrl+C / Ctrl+V
│       │
│       ├── Config/
│       │   ├── AppConfig.cs                    # POCO model for config.json
│       │   └── ConfigService.cs                # Load / save config.json
│       │
│       ├── UI/
│       │   └── SettingsForm.cs                 # WinForms settings dialog
│       │
│       ├── Interop/
│       │   └── NativeMethods.cs                # All P/Invoke signatures (User32, Kernel32)
│       │
│       └── Resources/
│           └── tray.ico                        # System tray icon (generated placeholder)
│
├── config.json                                 # User-editable config (created on first run)
└── OsCopyPaster.sln
```

---

## 5. Core Classes — Responsibilities & Contracts

### 5.1 `Program.cs`

```
[STAThread] Main()
  → Load config
  → Wire up dependencies
  → Application.EnableVisualStyles()
  → Application.SetCompatibleTextRenderingDefault(false)
  → Application.Run(new TrayApplication(...))
```

The composition root. No logic here — just wiring.

---

### 5.2 `TrayApplication` (extends `ApplicationContext`)

**Owns:** `NotifyIcon`, context menu, app lifecycle.

| Responsibility | Detail |
|---|---|
| Create `NotifyIcon` | Generated icon + tooltip, right-click `ContextMenuStrip` |
| Context Menu Items | **Toggle On/Off** (checkbox), **Settings** (opens `SettingsForm`), **Exit** |
| Balloon Notifications | Brief tooltip balloon on successful copy/paste (e.g., "Copied to slot 3 📋") |
| Dispose | Unhook keyboard hook, dispose NotifyIcon, tray cleanup |

Receives `AppState`, `GlobalKeyboardHook`, and `ConfigService` via constructor injection.

**Notification style:** Uses `NotifyIcon.ShowBalloonTip(durationMs: 1000, ...)` with `ToolTipIcon.None` for a clean, non-intrusive toast. Auto-dismisses in ~1 second.

---

### 5.3 `AppState`

A simple observable state container:

```
bool IsEnabled { get; set; }     // Toggle on/off
event Action<bool> OnToggled;    // Notify UI + hook
```

When `IsEnabled = false`, the keyboard hook callback short-circuits immediately (still installed, just ignores hotkeys). This avoids the cost of unhooking/rehooking, which can momentarily drop keystrokes.

---

### 5.4 `GlobalKeyboardHook`

Encapsulates `SetWindowsHookEx(WH_KEYBOARD_LL, ...)`, the hook callback, and the **chord state machine**.

```
Install()       → SetWindowsHookEx
Uninstall()     → UnhookWindowsHookEx
event Action<HotkeyAction> HotkeyPressed   // (Copy|Paste, slotNumber)
```

**State machine fields:**
```csharp
enum ChordState { Idle, AwaitingCopySlot, AwaitingPasteSlot }
private ChordState _chordState = ChordState.Idle;
private System.Windows.Forms.Timer _chordTimer;  // 1-second timeout
```

**Hook callback logic (simplified):**
```
if (_isSimulating) → CallNextHookEx (pass through)
if (!_appState.IsEnabled) → CallNextHookEx

switch (_chordState):
  case Idle:
    if key matches CopyCombo → suppress, set AwaitingCopySlot, start timer
    if key matches PasteCombo → suppress, set AwaitingPasteSlot, start timer
    else → CallNextHookEx

  case AwaitingCopySlot:
    if key is digit 0-9 → suppress, stop timer, post Copy(digit), reset to Idle
    else → stop timer, reset to Idle, CallNextHookEx (let key pass)

  case AwaitingPasteSlot:
    if key is digit 0-9 → suppress, stop timer, post Paste(digit), reset to Idle
    else → stop timer, reset to Idle, CallNextHookEx
```

**Timer timeout handler:**
When the 1-second timer fires, auto-select slot 1 and fire the corresponding action. This provides a convenient shortcut: `Ctrl+;` alone (without a digit) copies to slot 1.

**Critical details:**
- **Re-entrancy guard:** `bool _isSimulating` — set before `SendInput`, cleared after swap completes.
- **GC protection:** Delegate stored in class field `private readonly LowLevelKeyboardProc _hookProc;`
- **WinForms Timer:** Uses `System.Windows.Forms.Timer` (not `System.Threading.Timer`) because it fires on the UI/STA thread — no marshaling needed.

---

### 5.5 `HotkeyAction`

```csharp
record HotkeyAction(HotkeyType Type, int Slot);
enum HotkeyType { SecondaryCopy, SecondaryPaste }
```

---

### 5.6 `KeyCombo` (value type / record struct)

```csharp
record struct KeyCombo(Keys Modifiers, Keys Key);
// e.g. KeyCombo(Keys.Control, Keys.OemSemicolon)   → Ctrl + ;
// e.g. KeyCombo(Keys.Control, Keys.OemQuotes)       → Ctrl + '
```

Provides `Matches(int vkCode)` method that checks the vkCode against `Key` and calls `GetAsyncKeyState` for modifiers.

---

### 5.7 `ClipboardManager`

Low-level clipboard read/write with full data format preservation.

```
ClipboardBackup Save()          → Deep-copies ALL formats from current clipboard
void Restore(ClipboardBackup)   → Writes all formats back
void SetText(string text)       → Writes text to clipboard
string? GetText()               → Reads text from clipboard
void Clear()                    → Empties the clipboard
```

**`ClipboardBackup`** is a wrapper around `Dictionary<string, object>` — one entry per clipboard format. The `Save()` method iterates `IDataObject.GetFormats()` and deep-copies each format's data into a new `DataObject`. Per-format `try/catch` ensures one failing format (e.g., a custom COM format with delayed rendering) doesn't abort the entire backup.

**Retry logic:** Clipboard operations can throw `ExternalException` if another app has the clipboard locked. We retry up to 5 times with 50ms delays via `Clipboard.SetDataObject(data, copy: true, retryTimes: 5, retryDelay: 50)`.

---

### 5.8 `ClipboardSwapOrchestrator`

The heart of the application. Orchestrates the full hotkey-to-action sequences.

```csharp
private readonly Dictionary<int, string?> _slots = new();  // slots 0-9
private bool _isBusy = false;

async Task HandleSecondaryCopy(int slot)
async Task HandleSecondaryPaste(int slot)
```

#### Secondary Copy Flow (`Ctrl+;` → `3`)

```
User presses Ctrl+; then 3
  │
  ├─ Hook suppresses keys, posts SecondaryCopy(slot: 3) to STA thread
  │
  ▼
1. Guard: if _isBusy, return
2. Set _isBusy = true
3. Set _isSimulating = true on hook (re-entrancy guard)
4. backup = ClipboardManager.Save()          // save user's clipboard
5. ClipboardManager.Clear()
6. InputSimulator.SendCtrlC()                // simulate Ctrl+C into active app
7. await Task.Delay(swapDelayMs)             // wait for target app to write to clipboard
8. _slots[3] = ClipboardManager.GetText()    // read what was copied → store in slot 3
9. ClipboardManager.Restore(backup)          // restore user's original clipboard
10. Set _isSimulating = false
11. Set _isBusy = false
12. Raise event → TrayApp shows "Copied to slot 3 📋"
```

#### Secondary Paste Flow (`Ctrl+'` → `3`)

```
User presses Ctrl+' then 3
  │
  ├─ Hook suppresses keys, posts SecondaryPaste(slot: 3) to STA thread
  │
  ▼
1. Guard: if _isBusy || _slots[3] == null, return
2. Set _isBusy = true
3. Set _isSimulating = true on hook
4. backup = ClipboardManager.Save()          // save user's clipboard
5. ClipboardManager.SetText(_slots[3])       // put slot 3 on clipboard
6. InputSimulator.SendCtrlV()                // simulate Ctrl+V into active app
7. await Task.Delay(swapDelayMs)             // wait for target app to read clipboard
8. ClipboardManager.Restore(backup)          // restore user's original clipboard
9. Set _isSimulating = false
10. Set _isBusy = false
11. Raise event → TrayApp shows "Pasted from slot 3 📋"
```

#### Why `await Task.Delay(swapDelayMs)`?

After `SendInput` injects keystrokes, the target application processes them asynchronously via its own message queue. We must wait for the target app to actually read from (paste) or write to (copy) the clipboard before we restore it. Default 150ms is a conservative value that works for most applications. Configurable in `config.json`.

---

### 5.9 `InputSimulator`

Wraps `SendInput` from User32.dll to simulate keyboard input.

```
static void SendCtrlC()
static void SendCtrlV()
```

**Key detail — Modifier Key Release:** When the user presses `Ctrl+;`, the Ctrl key is physically held down. Before we simulate `Ctrl+C`, we must first send a synthetic **Ctrl key-up** event to prevent the target app from seeing a "double Ctrl". The full sequence:

```
SendCtrlC():
  1. Send KEY_UP for Ctrl  (release user's physical Ctrl from input stream)
  2. Send KEY_DOWN for Ctrl
  3. Send KEY_DOWN for C
  4. Send KEY_UP for C
  5. Send KEY_UP for Ctrl
```

All five events are sent as a single `SendInput` batch (atomic from the OS's perspective).

---

### 5.10 `SettingsForm` (WinForms Dialog)

A modal `Form` launched from the tray context menu.

**Layout:**
```
┌─────────────────────────────────────┐
│  OS-CopyPaster Settings            │
├─────────────────────────────────────┤
│                                     │
│  Copy Hotkey:  [ Ctrl + ;       ]   │  ← Click to rebind (captures next keypress)
│  Paste Hotkey: [ Ctrl + '       ]   │  ← Click to rebind
│                                     │
│  Swap Delay:   [ 150 ▲▼ ] ms       │  ← NumericUpDown (50–500)
│                                     │
│  Chord Timeout: [ 1000 ▲▼ ] ms     │  ← NumericUpDown (500–3000)
│                                     │
│         [ Save ]    [ Cancel ]      │
│                                     │
└─────────────────────────────────────┘
```

**Keybind capture:** When the user clicks a keybind field, it enters "listening" mode (text changes to "Press keys..."). The next `KeyDown` event captures the combo and displays it. `Escape` cancels.

**On Save:** Writes to `AppConfig`, calls `ConfigService.Save()`, raises an event so `GlobalKeyboardHook` reloads its key combos without restarting.

---

### 5.11 `AppConfig` + `ConfigService`

**`AppConfig`** (POCO):
```json
{
  "secondaryCopyKey": "Ctrl+OemSemicolon",
  "secondaryPasteKey": "Ctrl+OemQuotes",
  "swapDelayMs": 150,
  "chordTimeoutMs": 1000,
  "showNotifications": true
}
```

**`ConfigService`**:
- `Load()` → reads `config.json`, deserializes with `System.Text.Json`, returns `AppConfig`
- `Save(AppConfig)` → serializes and writes back
- `event Action<AppConfig> ConfigChanged` → fired after save, consumed by hook to reload keybinds
- Creates `config.json` with defaults if it doesn't exist
- Config path: same directory as the executable

---

### 5.12 `NativeMethods`

All P/Invoke declarations in one place:

```csharp
// Keyboard Hook
SetWindowsHookEx(WH_KEYBOARD_LL, ...)
UnhookWindowsHookEx(...)
CallNextHookEx(...)
GetModuleHandle(...)

// Key State
GetAsyncKeyState(...)

// Input Simulation
SendInput(...)

// Structures
KBDLLHOOKSTRUCT
INPUT, KEYBDINPUT
```

---

## 6. Communication & Data Flow

```
┌──────────────┐       config        ┌──────────────┐
│ ConfigService │ ──────────────────► │  AppConfig   │
└──────┬───────┘                     └──────┬───────┘
       │                                    │
       │ ConfigChanged event                │ keybinds + delays
       ▼                                    ▼
┌──────────────┐  HotkeyPressed    ┌─────────────────────┐
│ GlobalKeyboard│ ────────────────► │  ClipboardSwap      │
│ Hook + Chord │  (type, slot)     │  Orchestrator       │
│ State Machine│                   └────────┬────────────┘
└──────────────┘                            │
       ▲                                    │ uses
       │ AppState.IsEnabled         ┌───────┴────────┐
       │                            ▼                ▼
┌──────────────┐            ┌──────────────┐ ┌──────────────┐
│  AppState    │            │  Clipboard   │ │    Input     │
│ (on/off)     │            │  Manager     │ │  Simulator   │
└──────┬───────┘            └──────────────┘ └──────────────┘
       │
       │ toggled via
       ▼
┌──────────────┐  SwapCompleted   ┌──────────────┐
│ TrayApp      │ ◄────────────────│  Orchestrator │
│ (NotifyIcon) │  show balloon    │              │
└──────┬───────┘                  └──────────────┘
       │
       │ opens
       ▼
┌──────────────┐
│ SettingsForm │ ──► ConfigService.Save() ──► ConfigChanged event
└──────────────┘
```

---

## 7. Known Edge Cases & Mitigations

### 7.1 Clipboard Lock Contention
Other applications (browsers, Office, password managers) may hold the clipboard open. `Clipboard.SetDataObject` can throw `ExternalException`.
**Mitigation:** Built-in retry with exponential backoff (handled by WinForms overload parameters).

### 7.2 Non-Text Clipboard Content
V1 stores only text in the secondary slots. If the user's primary clipboard contains an image or file list, `Save()`/`Restore()` will still preserve it using `IDataObject` deep copy, so the user's main clipboard is never lost.
**Future:** V2 could store full `IDataObject` in slots to support images, files, etc.

### 7.3 Re-entrancy / Hook Seeing Its Own Input
When `InputSimulator.SendCtrlC()` fires, the keyboard hook will see `Ctrl` and `C` keystrokes.
**Mitigation:** `_isSimulating` flag checked at the top of the hook callback. The flag is set **before** `SendInput` and cleared **after** the clipboard swap completes.

### 7.4 Rapid-Fire Hotkey Presses
User mashes `Ctrl+;` rapidly while a swap is in progress.
**Mitigation:** `_isBusy` guard in `ClipboardSwapOrchestrator` drops any hotkey events that arrive while a swap is already in progress.

### 7.5 Chord Timeout vs. Fast Typists
If the chord timeout is too short, fast typists might miss it. If too long, it creates an awkward delay before defaulting to slot 1.
**Mitigation:** Default 1000ms, configurable in settings (range 500–3000ms).

### 7.6 Focus-Sensitive Applications (Games, Terminals)
Some apps intercept `Ctrl+C` differently (e.g., terminals send SIGINT, games ignore it).
**Mitigation:** Out of scope for V1. Users can configure different keybinds via Settings if conflicts arise.

### 7.7 UAC-Elevated Windows
A non-elevated process cannot hook into elevated (admin) windows due to UIPI (User Interface Privilege Isolation).
**Mitigation:** Document this limitation. Users can run the app as administrator if needed, but this is not the default to follow least-privilege principles.

### 7.8 GC Collecting the Hook Delegate
If the `LowLevelKeyboardProc` delegate is not stored in a field, the GC can collect it while the native hook still references it, causing a crash.
**Mitigation:** Store the delegate in a class-level field: `private readonly LowLevelKeyboardProc _hookProc;`

### 7.9 Pasting from Empty Slot
User tries `Ctrl+' → 5` but slot 5 has never been copied to.
**Mitigation:** `ClipboardSwapOrchestrator` checks `_slots.ContainsKey(slot)` — if empty, shows a balloon "Slot 5 is empty" and does nothing.

---

## 8. .csproj Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>           <!-- No console window -->
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ApplicationIcon>Resources\tray.ico</ApplicationIcon>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

`WinExe` output type suppresses the console window. `UseWindowsForms` enables `System.Windows.Forms` (needed for `NotifyIcon`, `Clipboard`, and the application message loop).

---

## 9. Summary of Dependencies

| Dependency | Purpose |
|---|---|
| .NET 8 SDK | Runtime |
| `System.Windows.Forms` | NotifyIcon, Clipboard, message loop, SettingsForm |
| `System.Drawing` | Tray icon generation (programmatic icon) |
| `System.Text.Json` | Config serialization |
| `User32.dll` (P/Invoke) | Keyboard hook, SendInput, GetAsyncKeyState |
| `Kernel32.dll` (P/Invoke) | GetModuleHandle |

**Zero NuGet packages.** Everything uses framework/OS APIs.

---

## 10. Tray Icon — Programmatic Generation

Instead of shipping a static `.ico` file, the tray icon is generated at runtime using `System.Drawing`:

- 16×16 bitmap
- Rounded rectangle background (accent color)
- "2" or "CB" text overlay (representing "secondary clipboard")
- Converted to `Icon` via `Icon.FromHandle(bitmap.GetHicon())`

This eliminates the need for an external asset file and allows future dynamic icon changes (e.g., different color when disabled).
