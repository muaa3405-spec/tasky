### Downloads

| File | For |
|---|---|
| **Tasky-…-Windows-Setup.exe** | Windows 10 (version 2004) or later, 64-bit |
| **Tasky-…-macOS-AppleSilicon.dmg** | Macs with Apple silicon (M1 and later), macOS 12 or later |
| **Tasky-…-macOS-Intel.dmg** | Intel Macs, macOS 12 or later |

### Installing

**Windows:** run the setup file. Windows SmartScreen may say it "protected your PC" because the installer isn't code-signed. Click **More info → Run anyway**.

**macOS:** open the `.dmg` and drag **Tasky** to **Applications**. The app isn't signed with an Apple Developer ID yet, so on first launch macOS says it can't check it for malicious software. To open it, **right-click Tasky → Open → Open**, or go to **System Settings → Privacy & Security → Open Anyway**. You only need to do this once.

Install it over your current Tasky: your tasks, notes and attachments are kept.

[Code signing policy](https://github.com/muaa3405-spec/tasky#code-signing-policy) · [Privacy policy](https://github.com/muaa3405-spec/tasky#privacy-policy)

### What's new
- **Undo.** Completing, deleting or postponing a task shows a message with an **Undo** button. **Ctrl+Z** (macOS: **⌘Z**) works too.
- **Postpone.** The new **⏭** button on a task moves it to **tomorrow** or **next week**.
- **Keyboard shortcuts** for everything: **N** new task, **/** search, **↑ ↓** pick a task, **Space** done, **E** edit, **T** timer, **P** postpone, **1 2 3** switch views, and more. Press **?** to see them all. They are also listed in Settings.
- **Good to know.** The first time you open this version, a short tips window shows the most useful features and the shortcuts.
- **Automatic daily backups** of your tasks, notes and settings; the last 14 days are kept. **Settings → Backups** lets you put an earlier day back, back up now, or open the backup folder.
- **Safe start.** If Tasky can't start, it now explains what happened and offers to restore the latest backup, instead of closing silently.
- **Export and import.** Export tasks to **CSV** (Excel, Numbers, Google Sheets) or tasks and notes to **Markdown**. Import tasks from a **CSV** file, including exports from Todoist and similar apps.
- **Print or save as PDF** today's list, this week's, or all open tasks.
- **Larger text** option in **Settings → Appearance**, and screen readers (Narrator, VoiceOver) now announce every icon button.
- **Update check (optional, off by default).** When you switch it on in **Settings → Updates**, Tasky asks GitHub once a day whether a newer version exists. Nothing about you or your tasks is sent.

### Improved
- Every build is now tested on Windows and macOS by opening every page with sample tasks and notes, so a problem like the 1.1.1 start-up crash can't reach an installer again.
- Tasky now runs on **.NET 10**, Microsoft's current long-term support version (.NET 8 support ends in November 2026).
