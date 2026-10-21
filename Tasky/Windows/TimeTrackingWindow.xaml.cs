using System.Windows;
using Tasky.Helpers;
using Tasky.ViewModels;

namespace Tasky.Windows;

public partial class TimeTrackingWindow : Window
{
    public TimeTrackingWindow(MainViewModel vm)
    {
        InitializeComponent();
        ThemeHelper.Attach(this);
        DataContext = vm;
    }
}
