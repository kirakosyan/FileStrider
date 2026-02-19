using System.Collections.ObjectModel;
using Avalonia.Media;

namespace FileStrider.MauiApp.Models;

/// <summary>
/// Represents an item in the treemap visualization.
/// </summary>
public class TreemapItem
{
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
    /// <summary>
    /// Calculates the layout for treemap items using the squarified algorithm.
    /// </summary>
    /// <param name="items">The items to layout.</param>
    /// <param name="bounds">The available space for the treemap.</param>
    /// <returns>The items with calculated bounds.</returns>
    public static List<TreemapItem> CalculateLayout(IEnumerable<TreemapItem> items, Avalonia.Rect bounds)
    {
        var sortedItems = items.OrderByDescending(i => i.Size).ToList();
        if (!sortedItems.Any())
            return new List<TreemapItem>();

        var totalSize = sortedItems.Sum(i => i.Size);
        if (totalSize == 0)
            return sortedItems;

        // Normalize sizes to the available area
        var totalArea = bounds.Width * bounds.Height;
        foreach (var item in sortedItems)
        {
            item.Percentage = (double)item.Size / totalSize * 100;
        }

        // Simple row-based layout for initial implementation
        CalculateSimpleLayout(sortedItems, bounds, totalSize);
        
        return sortedItems;
    }

    /// <summary>
    /// Simple row-based layout algorithm (can be enhanced with squarified algorithm later).
    /// </summary>
    private static void CalculateSimpleLayout(List<TreemapItem> items, Avalonia.Rect bounds, long totalSize)
    {
        if (!items.Any()) return;

        double currentY = bounds.Y;
        double currentX = bounds.X;
        double rowHeight = 0;
        double remainingWidth = bounds.Width;
        double remainingHeight = bounds.Height;
        
        var itemsInCurrentRow = new List<TreemapItem>();
        
        foreach (var item in items)
        {
            var area = (double)item.Size / totalSize * bounds.Width * bounds.Height;
            var width = Math.Min(Math.Sqrt(area), remainingWidth);
            
            // Ensure width is not zero to avoid NaN
            if (width <= 0) width = 1;
            
            var height = area / width;
            
            // Ensure height is reasonable
            if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0)
                height = 1;
            
            // Check if we should start a new row
            if (currentX + width > bounds.X + bounds.Width || 
                (itemsInCurrentRow.Any() && height > rowHeight * 2))
            {
                // Finalize current row
                FinalizeRow(itemsInCurrentRow, currentY, rowHeight, bounds.X, bounds.Width);
                
                currentY += rowHeight;
                remainingHeight -= rowHeight;
                currentX = bounds.X;
                rowHeight = 0;
                itemsInCurrentRow.Clear();
                
                // Recalculate for new row
                remainingWidth = bounds.Width;
                width = Math.Min(Math.Sqrt(area), remainingWidth);
                if (width <= 0) width = 1;
                height = area / width;
                if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0)
                    height = 1;
            }
            
            item.Bounds = new Avalonia.Rect(currentX, currentY, width, height);
            rowHeight = Math.Max(rowHeight, height);
            currentX += width;
            remainingWidth = bounds.X + bounds.Width - currentX;
            
            itemsInCurrentRow.Add(item);
        }
        
        // Finalize the last row
        if (itemsInCurrentRow.Any())
        {
            FinalizeRow(itemsInCurrentRow, currentY, rowHeight, bounds.X, bounds.Width);
        }
    }
    
    /// <summary>
    /// Adjusts the widths of items in a row to fill the available width.
    /// </summary>
    private static void FinalizeRow(List<TreemapItem> rowItems, double y, double height, double startX, double totalWidth)
    {
        if (!rowItems.Any()) return;
        
        var totalRowWidth = rowItems.Sum(i => i.Bounds.Width);
        var scale = totalWidth / totalRowWidth;
        
        double currentX = startX;
        foreach (var item in rowItems)
        {
            var scaledWidth = item.Bounds.Width * scale;
            item.Bounds = new Avalonia.Rect(currentX, y, scaledWidth, height);
            currentX += scaledWidth;
        }
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
