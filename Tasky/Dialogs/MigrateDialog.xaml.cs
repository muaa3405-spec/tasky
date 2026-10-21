using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Tasky.Helpers;
using Tasky.Services;
using Tasky.Services.Migration;

namespace Tasky.Dialogs;

/// <summary>
/// "Migrate to another computer" wizard.
/// Export: password → choose location → encrypted file.
/// Import: choose file → password → review → Tasky restarts with the imported data.
/// </summary>
public partial class MigrateDialog : Window
{
    private const string FileFilter = "Tasky migration file (*" + MigrationService.FileExtension + ")|*"
                                      + MigrationService.FileExtension;

    private readonly MigrationService _migration;
    private string? _importFile;
    private string? _exportedFile;
    private bool _busy;
    private bool _importStaged;
    private bool _importConfirmed;

    public MigrateDialog(MigrationService migration)
    {
        _migration = migration;
        InitializeComponent();
        ThemeHelper.Attach(this);
        DialogLayoutHelper.FitToWorkArea(this);
    }

    // ── Navigation ────────────────────────────────────────────────────────

    private void ShowStep(FrameworkElement step)
    {
        foreach (var panel in new FrameworkElement[]
                 { ChooseStep, ExportStep, ImportStep, ConfirmImportStep, WorkingStep, DoneStep })
            panel.Visibility = panel == step ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowWorking(string message)
    {
        WorkingLabel.Text = message;
        ShowStep(WorkingStep);
    }

    private void ChooseExport_Click(object sender, RoutedEventArgs e)
    {
        try { ExportSummary.Text = _migration.CurrentCounts().Summary; }
        catch (Exception ex) { CrashLog.Write("Could not count records for export", ex); ExportSummary.Text = "All your Tasky data"; }
        ExportPassword.Clear();
        ExportPasswordConfirm.Clear();
        HideError(ExportError);
        UpdateExportPasswordState();
        ShowStep(ExportStep);
        ExportPassword.Focus();
    }

    private void ChooseImport_Click(object sender, RoutedEventArgs e)
    {
        HideError(ImportError);
        ImportPassword.Clear();
        UpdateImportState();
        ShowStep(ImportStep);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(ChooseStep);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ── Export ────────────────────────────────────────────────────────────

    private void ExportPassword_Changed(object sender, RoutedEventArgs e) => UpdateExportPasswordState();

    private void UpdateExportPasswordState()
    {
        var (ok, hint) = CheckNewPassword(ExportPassword.Password, ExportPasswordConfirm.Password);
        ExportButton.IsEnabled = ok;
        ExportPasswordHint.Text = hint;
    }

    /// <summary>Rules for a new migration password, with a hint to show under the fields.</summary>
    private static (bool Ok, string Hint) CheckNewPassword(string password, string confirm)
    {
        if (password.Length < MigrationService.MinPasswordLength)
            return (false, $"Use at least {MigrationService.MinPasswordLength} characters.");
        if (confirm.Length == 0)
            return (false, "Type the password again to confirm it.");
        if (password != confirm)
            return (false, "The passwords don't match.");
        return (true, password.Length >= 14 ? "✔ Strong password." : "✔ Passwords match. Longer is stronger.");
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Save migration file",
            Filter     = FileFilter,
            DefaultExt = MigrationService.FileExtension,
            FileName   = $"Tasky-migration-{DateTime.Now:yyyy-MM-dd}{MigrationService.FileExtension}"
        };
        if (dialog.ShowDialog(this) != true) return;

        var password = ExportPassword.Password;
        App.ViewModel?.CheckpointRunningTasks();   // include the latest tracked time
        await RunBusy("Encrypting your data…", () => _migration.Export(dialog.FileName, password),
            onSuccess: () =>
            {
                _exportedFile  = dialog.FileName;
                DoneLabel.Text = $"Saved to {dialog.FileName}";
                ShowStep(DoneStep);
            },
            onError: message => { ShowStep(ExportStep); ShowError(ExportError, message); });
    }

    private void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_exportedFile is null) return;
        try { Process.Start("explorer.exe", $"/select,\"{_exportedFile}\""); }
        catch (Exception ex) { CrashLog.Write("Could not open Explorer", ex); }
    }

    // ── Import ────────────────────────────────────────────────────────────

    private void BrowseImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose your migration file", Filter = FileFilter };
        if (dialog.ShowDialog(this) != true) return;
        _importFile = dialog.FileName;
        ImportFileLabel.Text = Path.GetFileName(_importFile);
        ImportFileLabel.ToolTip = _importFile;
        HideError(ImportError);
        UpdateImportState();
        ImportPassword.Focus();
    }

    private void ImportInput_Changed(object sender, RoutedEventArgs e) => UpdateImportState();

    private void UpdateImportState()
        => UnlockButton.IsEnabled = _importFile is not null && ImportPassword.Password.Length > 0;

    private void ImportPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && UnlockButton.IsEnabled) Unlock_Click(sender, e);
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (_importFile is null) return;
        var file = _importFile;
        var password = ImportPassword.Password;
        MigrationManifest? manifest = null;

        await RunBusy("Unlocking and checking the file…", () => manifest = _migration.StageImport(file, password),
            onSuccess: () =>
            {
                _importStaged = true;
                ShowImportReview(manifest!);
            },
            onError: message =>
            {
                ShowStep(ImportStep);
                ShowError(ImportError, message);
                ImportPassword.SelectAll();
                ImportPassword.Focus();
            });
    }

    private void ShowImportReview(MigrationManifest manifest)
    {
        ImportCreatedLabel.Text = $"Exported on {manifest.CreatedAt:MMMM d, yyyy 'at' HH:mm}";
        ImportSummary.Text      = manifest.Counts.Summary;

        MigrationCounts? current = null;
        try { current = _migration.CurrentCounts(); } catch { /* treat as empty */ }
        bool hasData = current is { } c && (c.OpenTasks + c.CompletedTasks + c.Notes) > 0;
        ReplaceWarning.Text = hasData
            ? $"⚠  This replaces the Tasky data on this computer ({current!.Summary}). " +
              $"A safety copy of it is kept in {AppPaths.ImportBackupsFolder}."
            : "Tasky will restart and open with this data.";
        ShowStep(ConfirmImportStep);
    }

    private void ConfirmImport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _migration.ConfirmStagedImport();
            _importConfirmed = true;
            ShowWorking("Restarting Tasky…");
            MigrationService.RestartTasky();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Could not restart after import", ex);
            ShowStep(ConfirmImportStep);
            InAppNotificationService.Warning("Close and reopen Tasky to finish the import.");
        }
    }

    private void CancelImport_Click(object sender, RoutedEventArgs e)
    {
        _migration.CancelStagedImport();
        _importStaged = false;
        ShowStep(ChooseStep);
    }

    // ── Shared ────────────────────────────────────────────────────────────

    /// <summary>Runs slow work off the UI thread with a progress screen; reports a friendly error.</summary>
    private async Task RunBusy(string message, Action work, Action onSuccess, Action<string> onError)
    {
        _busy = true;
        ShowWorking(message);
        try
        {
            await Task.Run(work);
            onSuccess();
        }
        catch (MigrationFileException ex)
        {
            onError(ex.Message);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Migration failed", ex);
            onError(ex is IOException or UnauthorizedAccessException
                ? $"Couldn't read or write the file: {ex.Message}"
                : "Something went wrong. Details were written to the Tasky log.");
        }
        finally
        {
            _busy = false;
        }
    }

    private static void ShowError(System.Windows.Controls.TextBlock target, string message)
    {
        target.Text = message;
        target.Visibility = Visibility.Visible;
    }

    private static void HideError(System.Windows.Controls.TextBlock target) => target.Visibility = Visibility.Collapsed;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_busy) { e.Cancel = true; return; }   // never abandon a half-written file
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Unlocked but not confirmed: throw away the decrypted copy.
        if (_importStaged && !_importConfirmed) _migration.CancelStagedImport();
        base.OnClosed(e);
    }
}
