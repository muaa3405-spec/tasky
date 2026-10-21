# Tasky – Build & Install Guide

## Prerequisites

| Tool | Download |
|------|----------|
| .NET 8 SDK (x64) | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Visual Studio 2022 (Community+) **or** VS Code with C# Dev Kit | https://visualstudio.microsoft.com |
| Inno Setup 6 (for the installer only) | https://jrsoftware.org/isdl.php |

> **Windows SDK target:** The project targets `net8.0-windows10.0.19041.0` (Windows 10 v2004 or later).  
> Windows 11 is fully supported. Windows 10 19041+ required for WinRT toast notifications.

---

## Project Structure

```
Tasky/
├── Tasky.sln
├── Tasky/
│   ├── Tasky.csproj            ← .NET 8 WPF + WinForms, x64
│   ├── App.xaml / .cs          ← Startup, single-instance, tray bootstrap
│   ├── MainWindow.xaml / .cs   ← Main UI + system tray + event handlers
│   ├── Assets/
│   │   ├── icon.ico            ← App + tray icon; replace with your own .ico
│   │   ├── tasky-logo.png      ← Logo used in the UI
│   │   └── notification-banner.png ← Image shown in Windows notifications
│   ├── Models/                 ← TaskItem, ReminderItem (plain C# classes)
│   ├── Services/
│   │   ├── DatabaseService.cs  ← SQLite persistence (stores in %AppData%\Tasky\tasky.db)
│   │   ├── HotkeyService.cs    ← Win32 RegisterHotKey (Win+Shift+T)
│   │   ├── NotificationService.cs ← Windows toast via Microsoft.Toolkit.Uwp.Notifications
│   │   └── ReminderSchedulerService.cs ← Background timer, fires toast notifications
│   ├── ViewModels/
│   │   ├── MainViewModel.cs    ← Task list, add/complete/delete/active logic
│   │   ├── TaskItemViewModel.cs ← Wraps TaskItem + live Pomodoro DispatcherTimer
│   │   └── RelayCommand.cs     ← Lightweight ICommand implementation
│   ├── Converters/             ← XAML value converters (strikethrough, visibility, etc.)
│   ├── Behaviors/
│   │   └── DragDropBehavior.cs ← Drag-to-reorder attached behavior for ItemsControl
│   ├── Windows/
│   │   ├── QuickAddWindow      ← Win+Shift+T floating input
│   │   └── WidgetWindow        ← Always-on-top compact bar
│   ├── Dialogs/
│   │   ├── EditTaskDialog      ← Edit title & description
│   │   └── ReminderDialog      ← Set time / frequency / end-date reminder
│   └── Resources/
│       └── Styles.xaml         ← All WPF styles, colours, converters as StaticResources
└── installer/
    └── setup.iss               ← Inno Setup 6 script → produces TaskySetup.exe
```

---

## Step 1 – Restore & Build (Debug)

```powershell
cd Tasky          # the folder containing Tasky.sln
dotnet restore
dotnet build -c Debug
```

Or open `Tasky.sln` in Visual Studio 2022 → **Build → Build Solution**.

---

## Step 2 – Run

```powershell
dotnet run --project Tasky/Tasky.csproj
```

Or press **F5** in Visual Studio.

### First-run notes
- The SQLite database is created automatically at `%AppData%\Tasky\tasky.db`.
- A system tray icon appears (blue square with a "T").
- The global hotkey **Win + Shift + T** opens the Quick Add window from anywhere.

---

## Step 3 – Publish (self-contained, no install required)

```powershell
dotnet publish Tasky/Tasky.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -o Tasky/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish
```

This produces a folder with `Tasky.exe` plus all required DLLs.  
Copy the entire `publish/` folder to any Windows 10+ machine and run `Tasky.exe` – no .NET runtime needed on the target.

---

## Step 4 – Build the Windows Installer

1. Install **Inno Setup 6** on your machine.
2. Open `installer\setup.iss` in the Inno Setup IDE (or run from the command line):

```cmd
"C:\Program Files (x86)\Inno Setup 6\iscc.exe" installer\setup.iss
```

3. The installer appears at `installer\Output\TaskySetup.exe`.
4. Run `TaskySetup.exe` on any Windows 10/11 x64 machine – it installs to  
   `%ProgramFiles%\Tasky\` and optionally adds a desktop shortcut and a startup entry.

---

## Toast Notifications – Important Note

`Microsoft.Toolkit.Uwp.Notifications` requires the app to have an **Application User Model ID (AUMID)** registered in the Start Menu to fire toast notifications reliably.

- **When installed via the Inno Setup installer:** this is handled automatically because the Start Menu shortcut created by the installer gives the app a proper AUMID.  
- **When running directly from `dotnet run` or the publish folder:** the Toolkit's `ToastNotificationManagerCompat` registers a temporary AUMID automatically for unpackaged Win32 apps (it writes a COM registration to the registry). This works on Windows 10 19041+ without MSIX packaging.
- If toasts still don't appear, ensure **Focus Assist** / **Do Not Disturb** is off, and that "Notifications from apps" is enabled in Windows Settings → System → Notifications.

---

## Customising the Hotkey

Open `App.xaml.cs` and change the modifiers / key in the `Hotkey.Register(...)` call:

```csharp
// Current: Win + Shift + T
Hotkey.Register(HotkeyService.MOD_WIN | HotkeyService.MOD_SHIFT, (uint)'T', ...);

// Example: Ctrl + Alt + Q
Hotkey.Register(HotkeyService.MOD_CTRL | HotkeyService.MOD_ALT, (uint)'Q', ...);
```

Virtual key codes are standard Windows VK codes (`'A'`–`'Z'`, `0x70`–`0x7B` for F1–F12, etc.).

---

## Replacing the Tray Icon

1. Create or source a 16×16 and/or 32×32 `.ico` file.
2. Drop it at `Tasky/Assets/icon.ico` (overwrite).
3. Rebuild – the icon is embedded as a WPF Resource and also referenced by the `.csproj` `<ApplicationIcon>` property.

---

## Data Location

All task data is stored in:

```
%APPDATA%\Tasky\tasky.db      (SQLite)
```

To move everything to another PC, use **Settings → Migrate to another computer**
(see the README).

### Testing with sandbox data

Set the `TASKY_DATA_ROOT` environment variable to an empty folder to run Tasky
against throw-away data instead of your own. The sandboxed copy uses its own
single-instance lock, so it can run next to your normal Tasky:

```powershell
$env:TASKY_DATA_ROOT = "$env:TEMP\tasky-sandbox"
dotnet run --project Tasky/Tasky.csproj
```

---

## Feature Reference

| Feature | How it works |
|---------|-------------|
| Minimize to tray | `Window_Closing` cancels close and calls `Hide()`; tray icon stays visible |
| Restore from tray | Double-click tray icon or "Open Tasky" context menu item |
| Win+Shift+T hotkey | `HotkeyService` calls `RegisterHotKey` via P/Invoke on the main window HWND |
| Quick-Add window | `QuickAddWindow` — borderless, transparent, `Topmost`, closes on `Escape` or `Deactivated` |
| Drag-to-reorder | `DragDropBehavior` attached property on `ItemsControl` using WPF DragDrop API |
| Strikethrough on complete | `BoolToStrikethroughConverter` bound to `IsCompleted` |
| Active task green highlight | `ActiveToBackgroundConverter` bound to `IsActive` |
| Pomodoro timer | `DispatcherTimer` in `TaskItemViewModel`, ticks every 1 s while `IsActive = true` |
| Compact widget | `WidgetWindow` — `WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`, bottom-right corner |
| Reminders | `ReminderDialog` stores in SQLite; `ReminderSchedulerService` polls every 30 s |
| Toast notifications | `Microsoft.Toolkit.Uwp.Notifications` (Win32-compatible) |
| Completed auto-hide | `MainViewModel.CompleteTask` moves VM from `Tasks` → `CompletedTasks`; shown via toggle |
| SQLite persistence | `DatabaseService` in `%AppData%\Tasky\tasky.db`; WAL mode for concurrency |
| Single instance | Named `Mutex` in `App.OnStartup`; second launch shows a message and exits |
