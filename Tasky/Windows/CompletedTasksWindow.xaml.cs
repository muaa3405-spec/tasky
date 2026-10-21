using System.Collections.ObjectModel;
using System.Windows;
using Tasky.Dialogs;
using Tasky.Helpers;
using Tasky.ViewModels;

namespace Tasky.Windows;

public partial class CompletedTasksWindow : Window
{
    private readonly MainViewModel _vm;
    private string _activeGroup = "All";

    public CompletedTasksWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        ThemeHelper.Attach(this);
        DataContext = vm;
        // Refresh list when collection or search changes
        vm.CompletedTasks.CollectionChanged += (_, _) => RefreshList();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.CompletedSearch)) RefreshList(); };
        RefreshList();
    }

    // ─── Group filter ─────────────────────────────────────────────────────

    private void Group_Click(object sender, RoutedEventArgs e)
    {
        _activeGroup = (sender as System.Windows.Controls.RadioButton)?.Tag?.ToString() ?? "All";
        RefreshList();
    }

    private void RefreshList()
    {
        var search = _vm.CompletedSearch?.Trim() ?? string.Empty;

        IEnumerable<TaskItemViewModel> filtered = _vm.CompletedTasks;

        // Text search
        if (!string.IsNullOrEmpty(search))
            filtered = filtered.Where(v =>
                v.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.TaskNumberDisplay.Contains(search, StringComparison.OrdinalIgnoreCase));

        // Group filter
        // "This Week" = last 7 days including today, so it includes the "Today" sub-group too
        if (_activeGroup == "Today")
            filtered = filtered.Where(v => v.CompletedGroup == "Today");
        else if (_activeGroup == "This Week")
            filtered = filtered.Where(v => v.CompletedGroup == "Today" || v.CompletedGroup == "This Week");

        // Build grouped display list (group header string + task VMs)
        var items = new ObservableCollection<object>();
        var groups = filtered
            .GroupBy(v => v.CompletedGroup)
            .OrderBy(g => g.Key == "Today" ? 0 : g.Key == "This Week" ? 1 : 2);

        foreach (var g in groups)
        {
            items.Add($"── {g.Key} ──");
            foreach (var v in g)
                items.Add(v);
        }

        CompletedList.ItemsSource = items;
    }

    // ─── Event handlers ───────────────────────────────────────────────────

    private void Task_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (GetVm(sender) is { } vm) OpenDetail(vm);
    }

    private void Detail_Click(object sender, RoutedEventArgs e)
    {
        if (GetVm(sender) is { } vm) OpenDetail(vm);
    }

    private void OpenDetail(TaskItemViewModel vm)
    {
        // No owner — fully independent window.
        // Any owner relationship (even MainWindow) causes WPF to reactivate
        // the hidden MainWindow when this dialog closes, making it reappear.
        var dlg = new TaskDetailDialog(vm, _vm)
        {
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen
        };
        dlg.Show();
        dlg.Activate();
    }

    private void Uncheck_Click(object sender, RoutedEventArgs e)
    {
        if (GetVm(sender) is { } vm)
        {
            _vm.UncompleteTask(vm);
            RefreshList();
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (GetVm(sender) is not { } vm) return;
        var dlg = new ConfirmDialog("Delete Task",
            $"Permanently delete \"{vm.Title}\"?", "Delete", ConfirmType.Danger) { Owner = this };
        if (dlg.ShowDialog() == true) { _vm.DeleteTask(vm); RefreshList(); }
    }

    private static TaskItemViewModel? GetVm(object sender)
        => (sender as FrameworkElement)?.DataContext as TaskItemViewModel;
}
