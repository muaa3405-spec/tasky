using System.Windows;
using System.Windows.Input;
using Tasky.Helpers;
using Tasky.Services;

namespace Tasky.Dialogs;

public partial class ManualTimeDialog : Window
{
    public int? ResultMinutes { get; private set; }

    private int _hours   = 0;
    private int _minutes = 0;

    // Prevents re-entrant TextChanged updates while we're setting text programmatically
    private bool _updating;

    public ManualTimeDialog()
    {
        InitializeComponent();
        ThemeHelper.Attach(this);
        UpdateDisplay();
    }

    // ─── Preset (called before ShowDialog to pre-fill existing time) ──────

    /// <summary>Pre-fills the spinners with an existing duration.</summary>
    public void PresetMinutes(int totalMinutes)
    {
        if (totalMinutes <= 0) return;
        _hours   = Math.Min(totalMinutes / 60, 100);
        _minutes = totalMinutes % 60;
        UpdateDisplay();
    }

    // ─── Display sync ─────────────────────────────────────────────────────

    private void UpdateDisplay()
    {
        _updating = true;
        HoursBox.Text   = _hours.ToString();
        MinutesBox.Text = _minutes.ToString("D2");
        _updating = false;
    }

    // ─── Spinner arrow buttons ────────────────────────────────────────────

    private void HoursUp_Click(object sender, RoutedEventArgs e)
    {
        if (_hours < 100) { _hours++; UpdateDisplay(); }
    }

    private void HoursDown_Click(object sender, RoutedEventArgs e)
    {
        if (_hours > 0) { _hours--; UpdateDisplay(); }
    }

    private void MinutesUp_Click(object sender, RoutedEventArgs e)
    {
        _minutes = _minutes < 59 ? _minutes + 1 : 0;
        UpdateDisplay();
    }

    private void MinutesDown_Click(object sender, RoutedEventArgs e)
    {
        _minutes = _minutes > 0 ? _minutes - 1 : 59;
        UpdateDisplay();
    }

    // ─── Editable TextBox handlers ────────────────────────────────────────

    // Allow digits only
    private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !e.Text.All(char.IsDigit);

    private void HoursBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updating) return;
        if (int.TryParse(HoursBox.Text, out int h))
            _hours = Math.Clamp(h, 0, 100);
    }

    private void HoursBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Clamp and reformat on focus-leave
        _hours = Math.Clamp(_hours, 0, 100);
        UpdateDisplay();
    }

    private void MinutesBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updating) return;
        if (int.TryParse(MinutesBox.Text, out int m))
            _minutes = Math.Clamp(m, 0, 59);
    }

    private void MinutesBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _minutes = Math.Clamp(_minutes, 0, 59);
        UpdateDisplay();
    }

    // ─── Quick option ─────────────────────────────────────────────────────

    /// <summary>"No idea" — skip time tracking and complete immediately.</summary>
    private void NoIdea_Click(object sender, RoutedEventArgs e)
    {
        ResultMinutes = null;
        DialogResult  = true;
    }

    // ─── Save / Cancel ────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Flush any in-progress edits before reading
        if (HoursBox.IsFocused)   HoursBox_LostFocus(sender, e);
        if (MinutesBox.IsFocused) MinutesBox_LostFocus(sender, e);

        int total = _hours * 60 + _minutes;
        if (total == 0)
        {
            InAppNotificationService.Warning("Set at least 1 minute, or click 'No idea' to skip.");
            return;
        }
        ResultMinutes = total;
        DialogResult  = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ResultMinutes = null;
        DialogResult  = false;
    }
}
