using System.Windows;
using System.Windows.Controls;

namespace DemoMux;

// The first five children are the step strip; the final child holds live details.
internal sealed class SetupLayout : Panel
{
    private double stepsHeight;
    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? 1120 : available.Width;
        bool compact = width < 932;
        double stepWidth = compact ? width / 5 : 320;
        stepsHeight = 0;
        for (int i = 0; i < 5; i++)
        {
            Children[i].Measure(new Size(stepWidth, double.PositiveInfinity));
            stepsHeight = compact ? Math.Max(stepsHeight, Children[i].DesiredSize.Height) : stepsHeight + Children[i].DesiredSize.Height;
        }
        Children[5].Measure(new Size(compact ? width : Math.Max(0, width - 344), double.PositiveInfinity));
        return new Size(width, compact ? stepsHeight + 16 + Children[5].DesiredSize.Height : Math.Max(stepsHeight, Children[5].DesiredSize.Height));
    }
    protected override Size ArrangeOverride(Size size)
    {
        bool compact = size.Width < 932;
        double y = 0;
        for (int i = 0; i < 5; i++)
        {
            Children[i].Arrange(compact
                ? new Rect(i * size.Width / 5, 0, size.Width / 5, stepsHeight)
                : new Rect(0, y, 320, Children[i].DesiredSize.Height));
            y += Children[i].DesiredSize.Height;
        }
        Children[5].Arrange(compact
            ? new Rect(0, stepsHeight + 16, size.Width, Children[5].DesiredSize.Height)
            : new Rect(344, 0, Math.Max(0, size.Width - 344), Children[5].DesiredSize.Height));
        return size;
    }
}
