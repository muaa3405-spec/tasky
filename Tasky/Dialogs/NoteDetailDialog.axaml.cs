using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Tasky.Helpers;
using Tasky.Platform;
using Tasky.Services;
using Tasky.ViewModels;

namespace Tasky.Dialogs;

public partial class NoteDetailDialog : Window
{
    private readonly Models.Note _note;
    private readonly MainViewModel _vm;
    private bool _isDirty;

    /// <summary>Set once the user agreed to discard unsaved changes, so closing goes ahead.</summary>
    private bool _discardConfirmed;

    [Obsolete("XAML previewer only")]
    public NoteDetailDialog()
    {
        _note = new Models.Note();
        _vm   = null!;
        InitializeComponent();
    }

    public NoteDetailDialog(Models.Note note, MainViewModel vm)
    {
        _note = note;
        _vm   = vm;
        InitializeComponent();
        ThemeHelper.Attach(this);
        DialogLayoutHelper.FitToWorkArea(this);
        AddHandler(KeyDownEvent, Window_PreviewKeyDown, RoutingStrategies.Tunnel);

        // Load current values — suppress dirty-tracking during initial load
        _isDirty = false;
        TitleBox.Text = _note.Title;
        DescBox.Text  = _note.Description;
        TagsBox.Text  = _note.Tags;
        _isDirty = false;          // reset after TextChanged fires on load
        SaveBtn.IsEnabled = false;

        Attachments.FilesAdded      += AttachFiles;
        Attachments.ImagePasted     += AttachPastedImage;
        Attachments.DeleteRequested += RemoveAttachment;
        ReloadAttachments();
    }

    /// <summary>A text box's text; Avalonia may report an untouched box as null.</summary>
    private static string TextOf(TextBox box) => box.Text ?? string.Empty;

    // ─── Attachments (saved immediately) ──────────────────────────────────

    private void ReloadAttachments()
    {
        Attachments.SetAttachments(_vm.GetNoteAttachments(_note.Id));
        _vm.RefreshNoteAttachmentCount(_note);
    }

    private void AttachFiles(IReadOnlyList<string> paths)
    {
        int saved = paths.Count(path => _vm.SaveNoteAttachment(_note.Id, path));
        ReloadAttachments();
        if (saved > 0) InAppNotificationService.Success(saved == 1 ? "File attached." : $"{saved} files attached.");
    }

    private void AttachPastedImage(byte[] png)
    {
        if (_vm.SaveNoteAttachmentFromBytes(_note.Id, png, ".png"))
            InAppNotificationService.Success("Image pasted and attached.");
        ReloadAttachments();
    }

    private async void RemoveAttachment(Models.IAttachment attachment)
    {
        if (attachment is not Models.NoteAttachment noteAttachment) return;
        var confirm = new ConfirmDialog("Remove Attachment",
            $"Remove \"{attachment.FileName}\" from this note?", "Remove", ConfirmType.Warning);
        if (await confirm.ShowDialog<bool?>(this) != true) return;
        _vm.DeleteNoteAttachment(noteAttachment);
        ReloadAttachments();
    }

    /// <summary>
    /// Ctrl+V / ⌘V attaches a copied image or files. While typing in a text box, a
    /// clipboard that holds text still pastes as text.
    /// </summary>
    private void Window_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (!KeyGestures.IsPaste(e)) return;
        if (e.Source is Visual source && source.FindAncestorOfType<TextBox>(includeSelf: true) is { } textBox)
        {
            // Whether the clipboard holds text can only be read asynchronously,
            // so take over the paste and decide once it is known.
            e.Handled = true;
            PasteIntoTextBoxOrAttach(textBox);
            return;
        }
        e.Handled = Attachments.TryPaste();
    }

    private async void PasteIntoTextBoxOrAttach(TextBox textBox)
    {
        if (await ClipboardHasTextAsync()) textBox.Paste();
        else Attachments.TryPaste();
    }

    private async Task<bool> ClipboardHasTextAsync()
    {
        try
        {
            var clipboard = Clipboard;
            return clipboard is not null && !string.IsNullOrEmpty(await clipboard.TryGetTextAsync());
        }
        catch
        {
            return false; // another program is holding the clipboard
        }
    }

    // ─── Dirty tracking ───────────────────────────────────────────────────

    private void Field_Changed(object? sender, TextChangedEventArgs e)
    {
        // Fires during InitializeComponent too, before the boxes exist.
        if (TitleBox is null || DescBox is null || TagsBox is null || SaveBtn is null) return;
        if (!_isDirty)
        {
            // First real change after load
            _isDirty = TextOf(TitleBox) != _note.Title || TextOf(DescBox) != _note.Description;
        }
        bool hasChange = TextOf(TitleBox) != _note.Title || TextOf(DescBox) != _note.Description
                      || TextOf(TagsBox) != _note.Tags;
        SaveBtn.IsEnabled = hasChange;
    }

    // ─── Actions ──────────────────────────────────────────────────────────

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            InAppNotificationService.Warning("Title cannot be empty.");
            TitleBox.Focus();
            return;
        }
        _note.Title       = TextOf(TitleBox).Trim();
        _note.Description = TextOf(DescBox).Trim();
        _note.Tags        = TextOf(TagsBox).Trim();
        _vm.UpdateNote(_note);
        SaveBtn.IsEnabled = false;
        _isDirty = false;
        InAppNotificationService.Success("Note saved.");
    }

    private void CopyDesc_Click(object? sender, RoutedEventArgs e)
    {
        var text = DescBox.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!PlatformServices.Clipboard.SetText(text))
            {
                InAppNotificationService.Warning("Clipboard is busy — try again in a moment.");
                return;
            }
            InAppNotificationService.Info("Description copied to clipboard.");
        }
        else
        {
            InAppNotificationService.Warning("Description is empty — nothing to copy.");
        }
    }

    // The unsaved-changes prompt lives in OnClosing so it is asked only once
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Covers both the Close button and the window's close button.
        // The prompt is asynchronous, so cancel this close and close again once confirmed.
        if (SaveBtn.IsEnabled && !_discardConfirmed)
        {
            e.Cancel = true;
            ConfirmDiscardAndClose();
            return;
        }
        base.OnClosing(e);
    }

    private async void ConfirmDiscardAndClose()
    {
        var dlg = new ConfirmDialog("Unsaved Changes",
            "You have unsaved changes. Close without saving?",
            "Discard", ConfirmType.Warning);
        if (await dlg.ShowDialog<bool?>(this) != true) return;
        _discardConfirmed = true;
        Close();
    }
}
