using FileStrider.MauiApp.Models;
using Avalonia;
using Xunit;

namespace FileStrider.Tests;

public class TreemapTests
{
    [Fact]
    public void TreemapItem_Properties_CanBeSetAndRetrieved()
    {
        // Arrange & Act
        var item = new TreemapItem
        {
            Name = "Test File",
            Size = 1024,
            Percentage = 10.5,
            FullPath = "/test/path",
            IsFile = true,
            Category = "Documents"
        };

        // Assert
        Assert.Equal("Test File", item.Name);
        Assert.Equal(1024, item.Size);
        Assert.Equal(10.5, item.Percentage);
        Assert.Equal("/test/path", item.FullPath);
        Assert.True(item.IsFile);
        Assert.Equal("Documents", item.Category);
        Assert.NotNull(item.Color);
    }

    [Fact]
    public void TreemapLayout_CalculateLayout_HandlesEmptyCollection()
    {
        // Arrange
        var items = new List<TreemapItem>();
        var bounds = new Rect(0, 0, 100, 100);

        // Act
        var result = TreemapLayout.CalculateLayout(items, bounds);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void TreemapLayout_CalculateLayout_SetsItemBounds()
    {
        // Arrange
        var items = new List<TreemapItem>
        {
            new TreemapItem { Name = "Large File", Size = 1000, Percentage = 50 },
            new TreemapItem { Name = "Medium File", Size = 600, Percentage = 30 },
            new TreemapItem { Name = "Small File", Size = 400, Percentage = 20 }
        };
        var bounds = new Rect(0, 0, 100, 100);

        // Act
        var result = TreemapLayout.CalculateLayout(items, bounds);

        // Assert
        Assert.Equal(3, result.Count);
        
        // Debug: Check each item individually
        for (int i = 0; i < result.Count; i++)
        {
            var item = result[i];
            Assert.True(item.Bounds.Width >= 0, $"Item {i} ({item.Name}) width should be >= 0, but was {item.Bounds.Width}");
            Assert.True(item.Bounds.Height >= 0, $"Item {i} ({item.Name}) height should be >= 0, but was {item.Bounds.Height}");
            Assert.True(item.Bounds.X >= 0, $"Item {i} ({item.Name}) X should be >= 0, but was {item.Bounds.X}");
            Assert.True(item.Bounds.Y >= 0, $"Item {i} ({item.Name}) Y should be >= 0, but was {item.Bounds.Y}");
        }
    }

    [Fact]
    public void TreemapColors_GetColorForCategory_ReturnsConsistentColors()
    {
        // Arrange & Act
        var color1 = TreemapColors.GetColorForCategory("Documents");
        var color2 = TreemapColors.GetColorForCategory("Documents");
        var color3 = TreemapColors.GetColorForCategory("Images");

        // Assert
        Assert.Equal(color1, color2); // Same category should return same color
        Assert.NotEqual(color1, color3); // Different categories should return different colors
    }
}
