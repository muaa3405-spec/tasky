using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Tasky.Helpers;
using Tasky.Models;
using Tasky.Services;
using static Tasky.Services.InAppNotificationService;

namespace Tasky.Dialogs;

public partial class ReminderDialog : Window
{
    private readonly int _taskId;
    private readonly string _taskTitle;
    private readonly DatabaseService _db;

    [Obsolete("XAML previewer only")]
    public ReminderDialog()
    {
        _taskTitle = string.Empty;
        _db        = null!;
        InitializeComponent();
    }

    public ReminderDialog(int taskId, string taskTitle, DatabaseService db)
    {
        _taskId    = taskId;
        _taskTitle = taskTitle;
        _db        = db;
        InitializeComponent();
        ThemeHelper.Attach(this);
        TaskTitleLabel.Text = taskTitle;
    }

    private int _selectedDayOfWeek = (int)DateTime.Today.DayOfWeek; // default today's weekday
    private static readonly (string Label, int Value)[] Weekdays =
    {
        ("Mon",1),("Tue",2),("Wed",3),("Thu",4),("Fri",5),("Sat",6),("Sun",0)
    };

    private void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        // Default date to today
        ReminderDatePicker.SelectedDate     = DateTime.Today;
        ReminderDatePicker.DisplayDateStart = DateTime.Today;

        // Hour/minute pickers
        for (int h = 0; h < 24; h++)
            HourBox.Items.Add(new ComboBoxItem { Content = $"{h:D2}", Tag = h });
        for (int m = 0; m < 60; m += 5)
            MinuteBox.Items.Add(new ComboBoxItem { Content = $"{m:D2}", Tag = m });
        HourBox.SelectedIndex   = DateTime.Now.Hour;
        // Snap to nearest 5-min slot (index = minute / 5, capped at 11)
        MinuteBox.SelectedIndex = Math.Min(DateTime.Now.Minute / 5, MinuteBox.ItemCount - 1);

        // Day-of-week chips (Mon–Sun)
        foreach (var (label, val) in Weekdays)
        {
            var captured = val;
            var chip = new RadioButton
            {
                Content   = label,
                Tag       = val,
                GroupName = "WD",
                Theme     = UiResources.Theme("FilterChip"),
                Margin    = new Thickness(0, 0, 5, 0),
                IsChecked = val == _selectedDayOfWeek
            };
            chip.IsCheckedChanged += (_, _) =>
            {
                if (chip.IsChecked == true) _selectedDayOfWeek = captured;
            };
            WeekdayChips.Children.Add(chip);
        }

        // Day-of-month picker (1–31)
        for (int d = 1; d <= 31; d++)
            DayOfMonthBox.Items.Add(new ComboBoxItem { Content = $"{d}", Tag = d });
        DayOfMonthBox.SelectedIndex = DateTime.Today.Day - 1;

        // Load existing reminders
        // Auto-delete any past one-time reminders before showing the list
        var existing = _db.GetRemindersForTask(_taskId);
        foreach (var stale in existing.Where(r => r.NextFireTime <= DateTime.Now &&
                                                   r.Frequency == ReminderFrequency.Once))
            _db.DeleteReminder(stale.Id);
        existing = _db.GetRemindersForTask(_taskId);   // reload after cleanup

        if (existing.Count > 0)
        {
            ExistingSection.IsVisible = true;
            foreach (var r in existing)
            {
                // Show full date+time so user knows exactly when it triggers
                var fireStr = r.NextFireTime.Date == DateTime.Today
                    ? $"Today {r.NextFireTime:HH:mm}"
                    : r.NextFireTime.Date == DateTime.Today.AddDays(1)
                        ? $"Tomorrow {r.NextFireTime:HH:mm}"
                        : $"{r.NextFireTime:MMM d, HH:mm}";
                var text = $"{fireStr}  —  {r.Frequency}";
                if (r.EndDate.HasValue) text += $"  (until {r.EndDate.Value:MMM d yyyy})";
                ExistingList.Items.Add(new ListBoxItem { Content = text, Tag = r });
            }
            ExistingList.DoubleTapped += (_, _) =>
            {
                if (ExistingList.SelectedItem is ListBoxItem { Tag: ReminderItem del } item)
                {
                    _db.DeleteReminder(del.Id);
                    ExistingList.Items.Remove(item);
                    Success("Reminder deleted.");
                }
            };
        }
    }

    private void FreqBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (EndDatePanel is null) return;
        var tag = (FreqBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "0";
        EndDatePanel.IsVisible  = tag != "0";
        WeekdayPanel.IsVisible  = tag == "2"; // Weekly
        MonthDayPanel.IsVisible = tag == "3"; // Monthly
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        int hour = (HourBox.SelectedItem as ComboBoxItem)?.Tag is int h ? h : DateTime.Now.Hour;
        int min  = (MinuteBox.SelectedItem as ComboBoxItem)?.Tag is int m ? m : 0;

        // Use the picked date (defaults to today); block past fire times
        var pickedDate = ReminderDatePicker.SelectedDate ?? DateTime.Today;
        var fire = pickedDate.Date.AddHours(hour).AddMinutes(min);
        if (fire <= DateTime.Now)
        {
            InAppNotificationService.Warning("Please choose a future date and time.");
            return;
        }

        var freqTag = (FreqBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "0";
        var freq    = (ReminderFrequency)int.Parse(freqTag);
        DateTime? endDate = EndDatePicker.SelectedDate;

        // Day-specific scheduling
        int? targetDow = null;
        int? targetDom = null;
        string scheduleLabel = freq.ToString();

        if (freq == ReminderFrequency.Weekly)
        {
            targetDow = _selectedDayOfWeek;
            var dayName = System.Globalization.CultureInfo.CurrentCulture
                .DateTimeFormat.GetDayName((DayOfWeek)_selectedDayOfWeek);
            // Recalculate first fire to fall on the correct weekday
            int diff = ((_selectedDayOfWeek - (int)fire.DayOfWeek) + 7) % 7;
            fire = fire.AddDays(diff == 0 && fire <= DateTime.Now ? 7 : diff);
            scheduleLabel = $"Every {dayName}";
        }
        else if (freq == ReminderFrequency.Monthly)
        {
            int dom = (DayOfMonthBox.SelectedItem as ComboBoxItem)?.Tag is int d ? d : DateTime.Today.Day;
            targetDom = dom;
            // Set fire to the target day this month (or next if already passed)
            int clamped = Math.Min(dom, DateTime.DaysInMonth(fire.Year, fire.Month));
            fire = new DateTime(fire.Year, fire.Month, clamped, hour, min, 0);
            if (fire <= DateTime.Now)
            {
                var nm = fire.AddMonths(1);
                fire = new DateTime(nm.Year, nm.Month, Math.Min(dom, DateTime.DaysInMonth(nm.Year, nm.Month)), hour, min, 0);
            }
            scheduleLabel = $"{dom}{OrdinalSuffix(dom)} of each month";
        }

        _db.InsertReminder(new ReminderItem
        {
            TaskId          = _taskId,
            NextFireTime    = fire,
            Frequency       = freq,
            EndDate         = endDate,
            IsEnabled       = true,
            TaskTitle       = _taskTitle,
            TargetDayOfWeek = targetDow,
            DayOfMonth      = targetDom
        });

        Close(true);
        Success($"Reminder set — {scheduleLabel} at {fire:HH:mm}");

        static string OrdinalSuffix(int n) => (n % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" }
        };
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
