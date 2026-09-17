using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace TagTuner.App;

/// <summary>
/// Setzt seine Kinder nebeneinander und bricht um, wenn die Zeile voll ist.
///
/// WinUI bringt so etwas nicht mit: <c>VariableSizedWrapGrid</c> verlangt
/// feste Kachelgrößen, und Pillen sind so breit wie ihr Text.
/// </summary>
public sealed class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 6;
    public double VerticalSpacing { get; set; } = 6;

    protected override Size MeasureOverride(Size available)
    {
        var limit = double.IsInfinity(available.Width) ? double.MaxValue : available.Width;

        double x = 0, rowHeight = 0, widest = 0, total = 0;

        foreach (var child in Children)
        {
            child.Measure(new Size(limit, double.PositiveInfinity));
            var size = child.DesiredSize;

            // Ein einzelnes Kind, das allein schon zu breit ist, bekommt eine
            // eigene Zeile statt eines Umbruchs ins Nichts.
            if (x > 0 && x + size.Width > limit)
            {
                total += rowHeight + VerticalSpacing;
                widest = Math.Max(widest, x - HorizontalSpacing);
                x = 0;
                rowHeight = 0;
            }

            x += size.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, size.Height);
        }

        widest = Math.Max(widest, x - HorizontalSpacing);
        total += rowHeight;

        return new Size(
            double.IsInfinity(available.Width) ? Math.Max(0, widest) : available.Width,
            Math.Max(0, total));
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0, y = 0, rowHeight = 0;

        foreach (var child in Children)
        {
            var size = child.DesiredSize;

            if (x > 0 && x + size.Width > final.Width)
            {
                y += rowHeight + VerticalSpacing;
                x = 0;
                rowHeight = 0;
            }

            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, size.Height);
        }

        return final;
    }
}
