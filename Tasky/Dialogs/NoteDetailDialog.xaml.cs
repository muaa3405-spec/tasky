using System.Windows;
using System.Windows.Controls;
using Tasky.Helpers;
using Tasky.Services;
using Tasky.ViewModels;

namespace Tasky.Dialogs;

public partial class NoteDetailDialog : Window
{
    private readonly Models.Note _note;
    private readonly MainViewModel _vm;
    private bool _isDirty;

    public NoteDetailDialog(Models.Note note, MainViewModel vm)
    {
        _note = note;
        _vm   = vm;
        InitializeComponent();
        ThemeHelper.Attach(this);
        DialogLayoutHelper.FitToWorkArea(this);

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

    private void AttachPastedImage(System.Windows.Media.Imaging.BitmapSource image)
    {
        if (_vm.SaveNoteAttachmentFromBytes(_note.Id, ImageFiles.EncodePng(image), ".png"))
            InAppNotificationService.Success("Image pasted and attached.");
        ReloadAttachments();
    }

    private void RemoveAttachment(Models.IAttachment attachment)
    {
        if (attachment is not Models.NoteAttachment noteAttachment) return;
        var confirm = new ConfirmDialog("Remove Attachment",
            $"Remove \"{attachment.FileName}\" from this note?", "Remove", ConfirmType.Warning) { Owner = this };
        if (confirm.ShowDialog() != true) return;
        _vm.DeleteNoteAttachment(noteAttachment);
        ReloadAttachments();
    }

    /// <summary>
    /// Ctrl+V attaches a copied image or files. While typing in a text box, a
    /// clipboard that holds text still pastes as text.
    /// </summary>
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.V ||
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
        bool typingText = e.OriginalSource is System.Windows.Controls.TextBox && SafeClipboard.ContainsText();
        if (!typingText) e.Handled = Attachments.TryPaste();
    }

    // ─── Dirty tracking ───────────────────────────────────────────────────

    private void Field_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_isDirty)
        {
            // First real change after load
            _isDirty = TitleBox.Text != _note.Title || DescBox.Text != _note.Description;
        }
        bool hasChange = TitleBox.Text != _note.Title || DescBox.Text != _note.Description || TagsBox.Text != _note.Tags;
        SaveBtn.IsEnabled = hasChange;
    }

    // ─── Actions ──────────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            InAppNotificationService.Warning("Title cannot be empty.");
            TitleBox.Focus();
            return;
        }
        _note.Title       = TitleBox.Text.Trim();
        _note.Description = DescBox.Text.Trim();
        _note.Tags        = TagsBox.Text.Trim();
        _vm.UpdateNote(_note);
        SaveBtn.IsEnabled = false;
        _isDirty = false;
        InAppNotificationService.Success("Note saved.");
    }

    private void CopyDesc_Click(object sender, RoutedEventArgs e)
    {
        var text = DescBox.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!SafeClipboard.TrySetText(text))
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
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Covers both the Close button and the X button
        if (SaveBtn.IsEnabled)
        {
            var dlg = new ConfirmDialog("Unsaved Changes",
                "You have unsaved changes. Close without saving?",
                "Discard", ConfirmType.Warning) { Owner = this };
            if (dlg.ShowDialog() != true) { e.Cancel = true; return; }
        }
        base.OnClosing(e);
    }
}
