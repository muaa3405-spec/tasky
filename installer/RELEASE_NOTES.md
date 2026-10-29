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

### Fixed
- **Group filters were cut off** by the task search bar when you have groups. The group chips above the search bar are fully visible again.
- **Note and task windows were too wide inside**: the description box and other fields ran past the right edge. They now fit inside the window with the same margin on both sides.
- **Scrolling got stuck** on the Tasks, Notes, Settings, Stats and other pages whenever the mouse pointer was over the space between cards or near the edges. The mouse wheel and trackpad now scroll smoothly wherever the pointer is.
