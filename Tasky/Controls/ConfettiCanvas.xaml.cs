using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Tasky.Controls;

public partial class ConfettiCanvas : Canvas
{
    private static readonly string[] Colors =
    {
        "#FF6B6B", "#4ECDC4", "#45B7D1", "#FCD34D",
        "#A78BFA", "#34D399", "#F97316", "#EC4899"
    };

    private readonly Random _rng = new();

    public ConfettiCanvas() => InitializeComponent();

    public void Burst(Point origin)
    {
        const int count = 50;
        var particles = new List<(UIElement el, double vx, double vy, double rot, double rotSpeed)>();

        for (int i = 0; i < count; i++)
        {
            double angle = _rng.NextDouble() * Math.PI * 2;
            double speed = _rng.NextDouble() * 6 + 3;
            double vx    = Math.Cos(angle) * speed;
            double vy    = Math.Sin(angle) * speed - 4; // bias upward
            double size  = _rng.NextDouble() * 8 + 6;
            double rotSpeed = (_rng.NextDouble() - 0.5) * 10;
            var color   = (Color)ColorConverter.ConvertFromString(Colors[_rng.Next(Colors.Length)]);

            UIElement el;
            if (i % 3 == 0)
            {
                el = new Ellipse
                {
                    Width  = size,
                    Height = size,
                    Fill   = new SolidColorBrush(color)
                };
            }
            else
            {
                el = new Rectangle
                {
                    Width  = size,
                    Height = size * 0.5,
                    Fill   = new SolidColorBrush(color),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform       = new RotateTransform(_rng.NextDouble() * 360)
                };
            }

            SetLeft(el, origin.X - size / 2);
            SetTop(el,  origin.Y - size / 2);
            Children.Add(el);
            particles.Add((el, vx, vy, 0, rotSpeed));
        }

        int    frame    = 0;
        double gravity  = 0.35;
        const int totalFrames = 70; // ~2 s at 30 fps

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        var state = particles.Select(p => (p.el, p.vx, p.vy, p.rot, p.rotSpeed)).ToArray();

        timer.Tick += (_, _) =>
        {
            frame++;
            double progress = (double)frame / totalFrames;
            double opacity  = Math.Max(0, 1 - progress * 1.3);

            for (int i = 0; i < state.Length; i++)
            {
                var (el, vx, vy, rot, rotSpeed) = state[i];
                vy += gravity;
                double newX = GetLeft(el) + vx;
                double newY = GetTop(el)  + vy;
                rot += rotSpeed;

                SetLeft(el, newX);
                SetTop(el,  newY);
                el.Opacity = opacity;

                if (el.RenderTransform is RotateTransform rt)
                    rt.Angle = rot;

                state[i] = (el, vx, vy, rot, rotSpeed);
            }

            if (frame >= totalFrames)
            {
                timer.Stop();
                foreach (var (el, _, _, _, _) in state)
                    Children.Remove(el);
            }
        };

        timer.Start();
    }
}
