# OS-CopyPaster

A lightweight, background multi-buffer clipboard manager for Windows 11 / 10. 

**OS-CopyPaster** gives you **3 independent, dedicated clipboard slots** with their own copy and paste hotkeys, working across any application without ever overwriting your primary Windows clipboard (`Ctrl + C` / `Ctrl + V`).

---

## ✨ Features

- **3 Dedicated Clipboard Slots:** Store multiple snippets at once and paste them on demand without juggling clipboard history windows.
- **Non-Destructive Clipboard Swapping:** Transparently backs up and restores your primary OS clipboard during copy/paste operations.
- **Global Keyboard Hooks:** Works system-wide across browsers, code editors, terminals, documents, and chat apps.
- **System Tray App:** Runs quietly in the background with quick right-click access to toggle the tool, open settings, or exit.
- **Customizable Keybindings:** Rebind any slot using the interactive Settings GUI or via `config.json`.
- **Zero Heavy Dependencies:** Built on native Windows APIs (.NET 8 WinForms) for fast execution and minimal memory usage.

---

## ⌨️ Default Keybindings

| Slot | Action | Default Hotkey |
| :--- | :--- | :--- |
| **Slot 1** | **Copy** to Slot 1 | <kbd>Ctrl</kbd> + <kbd>;</kbd> |
| **Slot 1** | **Paste** from Slot 1 | <kbd>Ctrl</kbd> + <kbd>'</kbd> |
| **Slot 2** | **Copy** to Slot 2 | <kbd>Ctrl</kbd> + <kbd>9</kbd> |
| **Slot 2** | **Paste** from Slot 2 | <kbd>Ctrl</kbd> + <kbd>0</kbd> |
| **Slot 3** | **Copy** to Slot 3 | <kbd>Ctrl</kbd> + <kbd>[</kbd> |
| **Slot 3** | **Paste** from Slot 3 | <kbd>Ctrl</kbd> + <kbd>]</kbd> |

> 💡 **Your primary clipboard remains untouched:** Standard `Ctrl + C` and `Ctrl + V` will always retain their own copied content.

---

## 🚀 How to Use

1. **Start the application** (it runs minimized to the System Tray / notification area).
2. **Copying to a slot:**
   - Highlight any text in any app.
   - Press <kbd>Ctrl</kbd> + <kbd>;</kbd> (Slot 1), <kbd>Ctrl</kbd> + <kbd>9</kbd> (Slot 2), or <kbd>Ctrl</kbd> + <kbd>[</kbd> (Slot 3).
3. **Pasting from a slot:**
   - Click where you want to paste.
   - Press <kbd>Ctrl</kbd> + <kbd>'</kbd> (Slot 1), <kbd>Ctrl</kbd> + <kbd>0</kbd> (Slot 2), or <kbd>Ctrl</kbd> + <kbd>]</kbd> (Slot 3).

### System Tray Controls

Right-click the **OS-CopyPaster** icon in the Windows taskbar tray to:
- **Enabled (Toggle):** Temporarily pause or re-enable hotkey interception without closing the program.
- **Settings:** Open the configuration window.
- **Exit:** Completely close the application and unhook keyboard listeners.

---

## ⚙️ Configuration & Settings

You can customize hotkeys and behavior in two ways:

### 1. Settings Window (GUI)
1. Right-click the tray icon and select **Settings**.
2. Click any keybinding field and press your desired key combination.
3. Adjust **Swap Delay (ms)** if working with slower remote desktop or terminal applications (default is `250ms`).
4. Toggle **Show Notifications** if you want balloon notifications on copy/paste.
5. Click **Save**.

### 2. Manual Configuration (`config.json`)
Settings are persisted in `config.json` next to the executable:

```json
{
  "Slot1CopyKey": "Ctrl+OemSemicolon",
  "Slot1PasteKey": "Ctrl+OemQuotes",
  "Slot2CopyKey": "Ctrl+D9",
  "Slot2PasteKey": "Ctrl+D0",
  "Slot3CopyKey": "Ctrl+OemOpenBrackets",
  "Slot3PasteKey": "Ctrl+OemCloseBrackets",
  "SwapDelayMs": 250,
  "ShowNotifications": false
}
```

---

## 🛠️ Building & Running from Source

### Prerequisites
- Windows 10 or Windows 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Run in Development
```powershell
# From the repository root
dotnet run --project src/OsCopyPaster/OsCopyPaster.csproj
```

### Build a Release Executable
```powershell
dotnet publish src/OsCopyPaster/OsCopyPaster.csproj -c Release -r win-x64 --self-contained false -o ./publish
```
The compiled `OsCopyPaster.exe` will be located in the `./publish` directory.

---

## 🧠 Architecture Overview

- **`GlobalKeyboardHook`**: Uses Win32 `SetWindowsHookEx` (`WH_KEYBOARD_LL`) to intercept custom shortcuts globally without freezing the UI thread.
- **`ClipboardSwapOrchestrator`**: Coordinates swapping buffers: backs up current Windows clipboard contents in memory, loads the requested buffer, sends simulated input, and restores the original clipboard.
- **`InputSimulator`**: Dispatches synthetic keyboard input to trigger copy/paste in target applications while managing physical modifier states.
- **`TrayApplication`**: Manages the application lifecycle, context menu, and background execution.

---

## 📄 License
This project is open-source. Feel free to use and modify it as needed.
