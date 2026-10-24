# Tasky – Build & Install Guide

Tasky is one app for **Windows and macOS**. The UI is built with
[Avalonia](https://avaloniaui.net) (XAML, like WPF), so the same code and the same
design run on both systems. Features that need the operating system (tray icon,
global shortcut, idle detection, app usage, clipboard images, sounds) have a
Windows and a macOS implementation under `Tasky/Platform/`.

## Prerequisites

| Tool | Needed for | Download |
|------|------------|----------|
| .NET 8 SDK | building and running (Windows or macOS) | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Visual Studio 2022, Rider or VS Code with C# Dev Kit | editing (optional) | |
| Inno Setup 6 | the Windows installer only (Windows) | https://jrsoftware.org/isdl.php |
| Xcode Command Line Tools | the macOS app bundle only (`xcode-select --install`) | |

Supported systems: Windows 10 (version 2004) or later, 64-bit, and macOS 12 or later
(Apple silicon and Intel).

---

## Project Structure

```
Tasky/
├── Tasky.sln
├── Tasky/
│   ├── Tasky.csproj            ← .NET 8 + Avalonia, one project for Windows and macOS
│   ├── Program.cs              ← entry point, fonts
│   ├── App.axaml / .cs         ← startup, single-instance, theme, services
│   ├── MainWindow.axaml / .cs  ← main UI + tray + event handlers
│   ├── Assets/                 ← icon.ico (Windows), tasky-logo.png
│   ├── Models/                 ← TaskItem, ReminderItem, Note… (plain C# classes)
│   ├── Services/               ← database, reminders, idle monitor, app usage, migration…
│   ├── Platform/               ← one interface per OS feature
│   │   ├── PlatformServices.cs ← picks the Windows or macOS implementation at start-up
│   │   ├── Windows/            ← Win32: tray icon, Win+Shift+T, idle, clipboard, sounds…
│   │   └── MacOS/              ← AppKit / Carbon: menu-bar icon, Ctrl+Option+T, idle, clipboard…
│   ├── ViewModels/             ← MainViewModel, TaskItemViewModel, RelayCommand
│   ├── Behaviors/              ← drag-to-reorder for the task list
│   ├── Controls/ Dialogs/ Windows/   ← the other screens
│   ├── Helpers/                ← shared UI helpers (animations, file pickers, images…)
│   └── Resources/
│       ├── DarkTheme.axaml / LightTheme.axaml  ← colour palettes
│       └── Styles.axaml        ← all control styles
├── installer/setup.iss         ← Inno Setup 6 script → TaskySetup.exe (Windows)
└── scripts/package-macos.sh    ← builds Tasky.app and Tasky.dmg (macOS)
```

---

## Build & Run (Windows or macOS)

```bash
dotnet run --project Tasky/Tasky.csproj
```

Or open `Tasky.sln` in Visual Studio / Rider and press **F5**.

### First-run notes
- The database is created automatically (see [Data Location](#data-location)).
- A tray icon appears (Windows notification area / macOS menu bar).
- The Quick Add shortcut opens a small input window from anywhere:
  **Win + Shift + T** on Windows, **Control + Option + T** on macOS
  (Cmd + Shift + T is avoided because browsers use it to reopen tabs).

---

## Publish (self-contained, no .NET install needed on the target)

```bash
# Windows (64-bit)
dotnet publish Tasky/Tasky.csproj -c Release -r win-x64     -o publish/win-x64

# macOS — Apple silicon / Intel
dotnet publish Tasky/Tasky.csproj -c Release -r osx-arm64   -o publish/osx-arm64
dotnet publish Tasky/Tasky.csproj -c Release -r osx-x64     -o publish/osx-x64
```

The Windows folder contains `Tasky.exe` plus its DLLs; copy it anywhere and run it.

---

## Windows Installer (Inno Setup)

1. Publish for Windows into the folder the script expects:

   ```powershell
   dotnet publish Tasky/Tasky.csproj -c Release -r win-x64 -o Tasky/bin/Release/net8.0/win-x64/publish
   ```

2. Compile the installer:

   ```cmd
   "C:\Program Files (x86)\Inno Setup 6\iscc.exe" installer\setup.iss
   ```

3. The installer appears at `installer\Output\TaskySetup.exe`. It installs to
   `%ProgramFiles%\Tasky\` and optionally adds a desktop shortcut and a startup entry.

---

## macOS App (`Tasky.app` and `Tasky.dmg`)

Run on a Mac:

```bash
./scripts/package-macos.sh          # Apple silicon (arm64)
./scripts/package-macos.sh x64      # Intel
```

The script publishes Tasky, wraps it in `dist/Tasky.app` (with its icon and
`Info.plist`), signs it ad hoc so it runs on your own Mac, and builds `dist/Tasky.dmg`.

To give the app to other people without the "unidentified developer" warning, sign it
with an Apple Developer ID and notarize it (needs a paid Apple Developer account):

```bash
codesign --force --deep --options runtime --sign "Developer ID Application: <Your Name>" dist/Tasky.app
xcrun notarytool submit dist/Tasky.dmg --keychain-profile <profile> --wait
xcrun stapler staple dist/Tasky.dmg
```

Tasky needs **no special macOS permissions**: idle time, the frontmost app name, the
clipboard and the global shortcut all work without Accessibility or Screen Recording access.
To start Tasky at login, add it under **System Settings → General → Login Items**.

---

## Windows and macOS differences

The screens, layout, colours and behaviour are the same on both systems. Where an
OS works differently:

| Feature | Windows | macOS |
|---------|---------|-------|
| Tray icon | Notification area, double-click to open, balloon tips | Menu bar icon with the same menu; no balloon tips (macOS has none) |
| Quick Add shortcut | Win + Shift + T | Control + Option + T |
| Paste shortcut | Ctrl + V | ⌘ V |
| Reminder sounds | Windows Media sounds | Built-in macOS alert sounds (Glass, Ping…) |
| Meeting hours | Detected from microphone / camera use | Not available, so the Meeting cards are hidden |
| App usage | Foreground window, including browser web apps | Frontmost application |
| Emoji icons | Single-colour outlines (as always) | Apple's colour emoji |
| Close button | Hides to the tray | Hides; click the Dock or menu-bar icon to bring it back |

---

## Replacing the Icons

- **Windows:** replace `Tasky/Assets/icon.ico` (used for `Tasky.exe`, the window and the tray).
- **macOS:** replace `Tasky/Assets/tasky-logo.png` (512×512 or larger); the packaging
  script turns it into the app icon.

---

## Data Location

| System | Data (database, settings, attachments) | Logs and import safety copies |
|--------|----------------------------------------|-------------------------------|
| Windows | `%APPDATA%\Tasky\` | `%LOCALAPPDATA%\Tasky\` |
| macOS | `~/Library/Application Support/Tasky/` | `~/Library/Application Support/Tasky Local/` |

The database is `tasky.db` (SQLite). To move everything to another computer —
including from Windows to macOS and back — use **Settings → Migrate to another computer**
(see the README).

### Testing with sandbox data

Set the `TASKY_DATA_ROOT` environment variable to an empty folder to run Tasky
against throw-away data instead of your own. The sandboxed copy uses its own
single-instance lock, so it can run next to your normal Tasky:

```powershell
$env:TASKY_DATA_ROOT = "$env:TEMP\tasky-sandbox"
dotnet run --project Tasky/Tasky.csproj
```

```bash
TASKY_DATA_ROOT="$TMPDIR/tasky-sandbox" dotnet run --project Tasky/Tasky.csproj
```

---

## Feature Reference

| Feature | How it works |
|---------|-------------|
| Minimize to tray | `Window_Closing` cancels the close and hides the window; the tray icon stays |
| Restore from tray | Double-click the tray icon (Windows), the "Open Tasky" menu item, or the Dock icon (macOS) |
| Quick Add shortcut | `IGlobalHotkey`: Win32 `RegisterHotKey` on Windows, Carbon `RegisterEventHotKey` on macOS |
| Quick-Add window | `QuickAddWindow` — borderless, transparent, topmost, closes on Escape or when it loses focus |
| Drag-to-reorder | `DragDropBehavior` attached property on the task `ItemsControl` (pointer capture, ghost card, drop line) |
| Active task green highlight | `ActiveToBackgroundConverter` bound to `IsActive` |
| Timer | `DispatcherTimer` in `TaskItemViewModel` refreshes the display every second; time comes from the clock |
| Reminders | `ReminderDialog` stores them in SQLite; `ReminderSchedulerService` checks every minute |
| Reminder pop-ups | `ReminderToastWindow`, Tasky's own themed window (bottom-right of the screen) |
| Idle / lock / sleep pause | `IdleMonitorService` + `IUserActivity` (Windows session events / macOS polling) |
| SQLite persistence | `DatabaseService`, WAL mode |
| Single instance | Named mutex; a second launch signals the first (named event on Windows, local pipe on macOS) and exits |
