using Avalonia.Threading;

namespace Tasky;

/// <summary>CI smoke test hook (see <see cref="Services.SmokeTest"/>).</summary>
public partial class MainWindow
{
    private static readonly TimeSpan SmokePageDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>Opens every page in turn (so each one is laid out with real data), then the task list again.</summary>
    public void ShowEveryPageForSmokeTest(Action onFinished)
    {
        var pages = Enum.GetValues<AppPage>().Append(AppPage.Main).ToList();
        int next = 0;
        var timer = new DispatcherTimer { Interval = SmokePageDelay };
        timer.Tick += (_, _) =>
        {
            if (next < pages.Count)
            {
                NavigateTo(pages[next++]);
                return;
            }
            timer.Stop();
            onFinished();
        };
        timer.Start();
    }
}
