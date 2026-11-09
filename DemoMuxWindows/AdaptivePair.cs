using System.Windows;
using System.Windows.Controls;

namespace DemoMux;

// Reflows existing children during layout; never rebuilds inputs or running animations.
internal sealed class AdaptivePair : Panel
{
    private readonly double breakpoint;
    private readonly bool equal;
    private const double Gap = 16;
    internal AdaptivePair(UIElement first, UIElement second, double breakpoint, bool equal = false)
    {
        this.breakpoint = breakpoint; this.equal = equal;
        Children.Add(first); Children.Add(second);
    }
    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? breakpoint : available.Width;
        if (width < breakpoint)
        {
            foreach (UIElement child in Children) child.Measure(new Size(width, double.PositiveInfinity));
            return new Size(width, Children[0].DesiredSize.Height + Gap + Children[1].DesiredSize.Height);
        }
        Children[1].Measure(new Size(equal ? (width - Gap) / 2 : width, double.PositiveInfinity));
        var right = equal ? (width - Gap) / 2 : Children[1].DesiredSize.Width;
        Children[0].Measure(new Size(Math.Max(0, width - Gap - right), double.PositiveInfinity));
        return new Size(width, Math.Max(Children[0].DesiredSize.Height, Children[1].DesiredSize.Height));
    }
    protected override Size ArrangeOverride(Size size)
    {
        if (size.Width < breakpoint)
        {
            Children[0].Arrange(new Rect(0, 0, size.Width, Children[0].DesiredSize.Height));
            Children[1].Arrange(new Rect(0, Children[0].DesiredSize.Height + Gap, size.Width, Children[1].DesiredSize.Height));
        }
        else
        {
            var right = equal ? (size.Width - Gap) / 2 : Children[1].DesiredSize.Width;
            Children[0].Arrange(new Rect(0, 0, Math.Max(0, size.Width - Gap - right), Children[0].DesiredSize.Height));
            Children[1].Arrange(new Rect(size.Width - right, 0, right, Children[1].DesiredSize.Height));
        }
        return size;
    }
}
