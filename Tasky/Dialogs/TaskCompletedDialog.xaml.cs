using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Tasky.Helpers;
using Tasky.Services;
using Tasky.ViewModels;

namespace Tasky.Dialogs;

public partial class TaskCompletedDialog : Window
{
    private readonly TaskItemViewModel _vm;

    public TaskCompletedDialog(TaskItemViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DialogLayoutHelper.FitToWorkArea(this);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PopulateDetails();
        var sb = (Storyboard)Resources["FadeIn"];
        Storyboard.SetTarget(sb, CardRoot);
        sb.Begin();
        ConfettiLayer.UpdateLayout();
        var center = new Point(ConfettiLayer.ActualWidth / 2, ConfettiLayer.ActualHeight / 2);
        ConfettiLayer.Burst(center);
    }

    private void PopulateDetails()
    {
        var completedAt = _vm.CompletedAt ?? DateTime.Now;
        var dateStr     = completedAt.ToString("MMM d, yyyy  HH:mm");

        TaskNumberLabel.Text = _vm.TaskNumberDisplay;
        TitleLabel.Text      = _vm.Title;
        SubtitleLabel.Text   = "Great work! Keep up the momentum.";
        DateLabel.Text       = dateStr;

        if (!string.IsNullOrWhiteSpace(_vm.Description))
        {
            DescLabel.Text       = _vm.Description;
            DescLabel.Visibility = Visibility.Visible;
        }
        if (!string.IsNullOrEmpty(_vm.GroupBadge))
        {
            GroupLabel.Text       = _vm.GroupBadge;
            GroupBadge.Background = new SolidColorBrush(_vm.GroupColor);
            GroupBadge.Visibility = Visibility.Visible;
        }

        // Banner fields
        BannerTaskNum.Text = _vm.TaskNumberDisplay;
        BannerTitle.Text   = _vm.Title;
        BannerDate.Text    = $"Completed on  {dateStr}";
    }

    private void Screenshot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Make the banner visible and let it size to its own content, decoupled
            // from the dialog window's width, so the capture is a tight, small banner
            // with no empty background around it.
            BannerCapture.Visibility = Visibility.Visible;
            BannerCapture.Measure(new System.Windows.Size(
                double.PositiveInfinity, double.PositiveInfinity));
            var size = BannerCapture.DesiredSize;
            BannerCapture.Arrange(new Rect(new System.Windows.Point(0, 0), size));
            BannerCapture.UpdateLayout();
            DoEvents();

            double bannerW = size.Width, bannerH = size.Height;
            const double scale = 3.0;
            int w = (int)Math.Ceiling(bannerW * scale);
            int h = (int)Math.Ceiling(bannerH * scale);
            var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);

            // Draw only the banner's content bounds — nothing around it.
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Fill with the banner background colour first so no transparent
                // (black) pixels remain around the edges.
                var bg = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#0F172A"));
                dc.DrawRectangle(bg, null, new Rect(0, 0, bannerW, bannerH));

                var vb = new System.Windows.Media.VisualBrush(BannerCapture)
                {
                    Stretch     = System.Windows.Media.Stretch.None,
                    AlignmentX  = AlignmentX.Left,
                    AlignmentY  = AlignmentY.Top
                };
                dc.DrawRectangle(vb, null, new Rect(0, 0, bannerW, bannerH));
            }
            rtb.Render(dv);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            SafeClipboard.TrySetImage(rtb);

            string message;
            if (App.Settings.Current.SaveScreenshotToDesktop)
            {
                var desktop  = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var filename = $"Tasky_{_vm.TaskNumberDisplay}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                using (var fs = File.Create(Path.Combine(desktop, filename))) encoder.Save(fs);
                message = "Banner saved to Desktop & copied to clipboard.";
            }
            else
            {
                message = "Banner copied to clipboard.";
            }

            Close();
            InAppNotificationService.Success(message);
        }
        catch (Exception ex)
        {
            ActionButtons.Visibility = Visibility.Visible;
            BannerCapture.Visibility = Visibility.Collapsed;
            InAppNotificationService.Error($"Screenshot failed: {ex.Message}");
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private static void DoEvents() =>
        System.Windows.Application.Current.Dispatcher.Invoke(
            System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        base.OnKeyDown(e);
    }
}
