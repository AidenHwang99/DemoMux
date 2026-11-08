using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
namespace DemoMux;

// A compositor-driven transform: countdown text updates never restart this animation.
internal sealed class VerificationActivity : Grid
{
    private readonly TranslateTransform travel = new();
    public VerificationActivity()
    {
        Height = 4; Margin = new Thickness(0, 5, 0, 3); ClipToBounds = true; IsHitTestVisible = false;
        Children.Add(new Border { Height = 2, Background = Theme.Border, VerticalAlignment = VerticalAlignment.Center });
        var color = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5), GradientStops = {
            new(Colors.Transparent,0), new(Theme.Accent.Color,.35), new(Theme.Link.Color,.5), new(Theme.Accent.Color,.65), new(Colors.Transparent,1)
        } };
        var beam = new Grid { Width = 180, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = travel };
        beam.Children.Add(new Rectangle { Fill = color, Opacity = .25 });
        beam.Children.Add(new Rectangle { Fill = color, Height = 2, VerticalAlignment = VerticalAlignment.Center });
        Children.Add(beam);
        Loaded += (_, _) => Animate(); SizeChanged += (_, _) => { if (IsLoaded) Animate(); };
        Unloaded += (_, _) => travel.BeginAnimation(TranslateTransform.XProperty, null);
    }
    private void Animate()
    {
        travel.BeginAnimation(TranslateTransform.XProperty, null);
        if (!SystemParameters.ClientAreaAnimation) { travel.X = Math.Max(0, (ActualWidth - 180) / 2); return; }
        var motion = new DoubleAnimation(-180, ActualWidth + 180, TimeSpan.FromSeconds(2.4)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(motion, 60);
        travel.BeginAnimation(TranslateTransform.XProperty, motion);
    }
}
