using System.Collections.ObjectModel;
using Avalonia.Media;

namespace FileStrider.MauiApp.Models;

/// <summary>
/// Represents an item in the treemap visualization.
/// </summary>
public class TreemapItem
{
    public bool IsAggregate { get; set; }
    /// <summary>
    /// Gets or sets the name/label of the item.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the size value that determines the rectangle size.
    /// </summary>
    public long Size { get; set; }
    
    /// <summary>
    /// Gets or sets the full path of the file or folder.
    /// </summary>
    public string FullPath { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the color to use for this item in the treemap.
    /// </summary>
    public IBrush Color { get; set; } = Brushes.LightBlue;
    
    /// <summary>
    /// Gets or sets the rectangle bounds for this item (set by the layout algorithm).
    /// </summary>
    public Avalonia.Rect Bounds { get; set; }
    
    /// <summary>
    /// Gets or sets whether this item is a file or folder.
    /// </summary>
    public bool IsFile { get; set; }
    
    /// <summary>
    /// Gets or sets the file type category for color coding.
    /// </summary>
    public string Category { get; set; } = "Other";
    
    /// <summary>
    /// Gets or sets the percentage of total size this item represents.
    /// </summary>
    public double Percentage { get; set; }
}

/// <summary>
/// Layout algorithm for positioning treemap rectangles.
/// </summary>
public static class TreemapLayout
{
    public static List<TreemapItem> CalculateLayout(IEnumerable<TreemapItem> items, Avalonia.Rect bounds)
    {
        var all = items.ToList();
        foreach (var item in all) { item.Bounds = default; item.Percentage = 0; }
        var positive = all.Where(i => i.Size > 0).OrderByDescending(i => i.Size).ToList();
        // Every range total is exact and available in O(1), including tiny ranges
        // beside dominant items. Decimal can hold the sum of any List<long>.
        var sums = new decimal[positive.Count + 1];
        for (var i = 0; i < positive.Count; i++) sums[i + 1] = sums[i] + positive[i].Size;
        var total = sums[^1];
        if (total == 0 || bounds.Width <= 0 || bounds.Height <= 0 ||
            !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height)) return all;
        foreach (var item in positive) item.Percentage = (double)(100m * item.Size / total);
        Split(positive, sums, 0, positive.Count, bounds);
        return all;
    }

    // Balanced binary partitions preserve area exactly and keep every tile inside its parent.
    private static void Split(List<TreemapItem> items, decimal[] sums, int start, int count, Avalonia.Rect bounds)
    {
        if (count == 1) { items[start].Bounds = bounds; return; }
        var total = sums[start + count] - sums[start];
        var leftCount = 1;
        while (leftCount < count - 1 && sums[start + leftCount + 1] - sums[start] <= total / 2)
            leftCount++;
        var leftSize = sums[start + leftCount] - sums[start];
        var ratio = (double)(leftSize / total);
        Avalonia.Rect left, right;
        if (bounds.Width >= bounds.Height)
        {
            var width = bounds.Width * ratio;
            left = new(bounds.X, bounds.Y, width, bounds.Height);
            right = new(bounds.X + width, bounds.Y, Math.Max(0, bounds.Width - width), bounds.Height);
        }
        else
        {
            var height = bounds.Height * ratio;
            left = new(bounds.X, bounds.Y, bounds.Width, height);
            right = new(bounds.X, bounds.Y + height, bounds.Width, Math.Max(0, bounds.Height - height));
        }
        Split(items, sums, start, leftCount, left);
        Split(items, sums, start + leftCount, count - leftCount, right);
    }
}

/// <summary>
/// Color scheme provider for treemap items.
/// </summary>
public static class TreemapColors
{
    private static readonly Dictionary<string, IBrush> CategoryColors = new()
    {
        ["Images"] = new SolidColorBrush(Color.FromRgb(255, 159, 64)),     // Orange
        ["Videos"] = new SolidColorBrush(Color.FromRgb(255, 99, 132)),     // Red
        ["Audio"] = new SolidColorBrush(Color.FromRgb(153, 102, 255)),     // Purple
        ["Documents"] = new SolidColorBrush(Color.FromRgb(75, 192, 192)),  // Teal
        ["Archives"] = new SolidColorBrush(Color.FromRgb(255, 205, 86)),   // Yellow
        ["Code"] = new SolidColorBrush(Color.FromRgb(54, 162, 235)),       // Blue
        ["Executables"] = new SolidColorBrush(Color.FromRgb(201, 203, 207)), // Gray
        ["Folders"] = new SolidColorBrush(Color.FromRgb(120, 180, 120)),   // Green
        ["Other"] = new SolidColorBrush(Color.FromRgb(200, 200, 200))      // Light Gray
    };
    
    /// <summary>
    /// Gets the color for a specific category.
    /// </summary>
    /// <param name="category">The category name.</param>
    /// <returns>The brush to use for that category.</returns>
    public static IBrush GetColorForCategory(string category)
    {
        return CategoryColors.TryGetValue(category, out var color) 
            ? color 
            : CategoryColors["Other"];
    }
    
    /// <summary>
    /// Gets a lighter version of the category color for hover effects.
    /// </summary>
    /// <param name="category">The category name.</param>
    /// <returns>A lighter version of the category color.</returns>
    public static IBrush GetLightColorForCategory(string category)
    {
        var baseColor = GetColorForCategory(category);
        if (baseColor is SolidColorBrush solidBrush)
        {
            var color = solidBrush.Color;
            var lightColor = Color.FromArgb(
                color.A,
                (byte)Math.Min(255, color.R + 40),
                (byte)Math.Min(255, color.G + 40),
                (byte)Math.Min(255, color.B + 40)
            );
            return new SolidColorBrush(lightColor);
        }
        return baseColor;
    }
}
