using System.Windows;
using System.Windows.Input;
using Tasky.ViewModels;

namespace Tasky.Windows;

public partial class QuickAddWindow : Window
{
    private readonly MainViewModel _vm;

    public QuickAddWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
    }

    private void TitleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Commit();
        else if (e.Key == Key.Escape) Close();
    }

    private void Add_Click(object sender, RoutedEventArgs e) => Commit();

    private void Commit()
    {
        var text = TitleBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(text))
            _vm.QuickAdd(text);
        Close();
    }

    private void Window_Deactivated(object sender, EventArgs e) => Close();
}
