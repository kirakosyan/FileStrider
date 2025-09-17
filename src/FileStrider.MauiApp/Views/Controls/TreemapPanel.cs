using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FileStrider.MauiApp.Views.Controls;

public class TreemapPanel : Panel
{
    public static readonly AttachedProperty<double> WeightProperty =
        AvaloniaProperty.RegisterAttached<TreemapPanel, Control, double>("Weight", 1d);

    public static double GetWeight(Control control) => control.GetValue(WeightProperty);

    public static void SetWeight(Control control, double value) => control.SetValue(WeightProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(availableSize);
        }

        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0 || finalSize.Width <= 0 || finalSize.Height <= 0)
        {
            return finalSize;
        }

        var totalWeight = Children.Sum(child => Math.Max(child.GetValue(WeightProperty), 0.0));
        if (totalWeight <= 0)
        {
            foreach (var child in Children)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
            }
            return finalSize;
        }

        double remainingWidth = finalSize.Width;
        double remainingHeight = finalSize.Height;
        double offsetX = 0;
        double offsetY = 0;
        double accumulatedWeight = totalWeight;
        bool horizontal = finalSize.Width >= finalSize.Height;

        foreach (var child in Children)
        {
            var weight = Math.Max(child.GetValue(WeightProperty), 0.0);
            if (weight <= 0)
            {
                child.Arrange(new Rect(offsetX, offsetY, 0, 0));
                continue;
            }

            var fraction = weight / accumulatedWeight;
            if (horizontal)
            {
                var childWidth = remainingWidth * fraction;
                child.Arrange(new Rect(offsetX, offsetY, childWidth, remainingHeight));
                offsetX += childWidth;
                remainingWidth -= childWidth;
            }
            else
            {
                var childHeight = remainingHeight * fraction;
                child.Arrange(new Rect(offsetX, offsetY, remainingWidth, childHeight));
                offsetY += childHeight;
                remainingHeight -= childHeight;
            }

            accumulatedWeight -= weight;
            if (accumulatedWeight <= 0)
            {
                break;
            }

            // horizontal = remainingWidth >= remainingHeight; // Removed to keep direction constant per level
        }

        return finalSize;
    }
}
