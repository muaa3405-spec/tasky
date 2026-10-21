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
