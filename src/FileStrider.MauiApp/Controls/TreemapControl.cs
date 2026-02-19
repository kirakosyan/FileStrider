using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using FileStrider.MauiApp.Models;
using System.Collections.ObjectModel;

namespace FileStrider.MauiApp.Controls;

/// <summary>
/// Custom control that renders a treemap visualization of file/folder sizes.
/// </summary>
public class TreemapControl : Control
{
    /// <summary>
    /// Defines the Items property.
    /// </summary>
    public static readonly StyledProperty<ObservableCollection<TreemapItem>?> ItemsProperty =
        AvaloniaProperty.Register<TreemapControl, ObservableCollection<TreemapItem>?>(nameof(Items));

    /// <summary>
    /// Defines the SelectedItem property.
    /// </summary>
    public static readonly StyledProperty<TreemapItem?> SelectedItemProperty =
        AvaloniaProperty.Register<TreemapControl, TreemapItem?>(nameof(SelectedItem));

    /// <summary>
    /// Defines the EmptyText property.
    /// </summary>
    public static readonly StyledProperty<string> EmptyTextProperty =
        AvaloniaProperty.Register<TreemapControl, string>(nameof(EmptyText), "No data to display. Run a scan to see the treemap.");

    private TreemapItem? _hoveredItem;

    /// <summary>
    /// Gets or sets the collection of items to display in the treemap.
    /// </summary>
    public ObservableCollection<TreemapItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>
    /// Gets or sets the currently selected item.
    /// </summary>
    public TreemapItem? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets or sets the text to display when there are no items.
    /// </summary>
    public string EmptyText
    {
        get => GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    /// <summary>
    /// Event raised when an item is clicked.
    /// </summary>
    public event EventHandler<TreemapItem>? ItemClicked;

    static TreemapControl()
    {
        AffectsRender<TreemapControl>(ItemsProperty, SelectedItemProperty, EmptyTextProperty);
        AffectsMeasure<TreemapControl>(ItemsProperty);
    }

    public TreemapControl()
    {
        // Set background in render method instead
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsProperty)
        {
            if (change.OldValue is ObservableCollection<TreemapItem> oldItems)
            {
                oldItems.CollectionChanged -= OnItemsCollectionChanged;
            }

            if (change.NewValue is ObservableCollection<TreemapItem> newItems)
            {
                newItems.CollectionChanged += OnItemsCollectionChanged;
            }

            InvalidateVisual();
        }
    }

    private void OnItemsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        // Draw background
        context.FillRectangle(Brushes.White, new Rect(0, 0, Bounds.Width, Bounds.Height));

        base.Render(context);

        if (Items == null || !Items.Any())
        {
            // Draw empty state
            var emptyText = new FormattedText(
                EmptyText,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                14,
                Brushes.Gray);

            var textRect = new Rect(
                (Bounds.Width - emptyText.Width) / 2,
                (Bounds.Height - emptyText.Height) / 2,
                emptyText.Width,
                emptyText.Height);

            context.DrawText(emptyText, textRect.TopLeft);
            return;
        }

        // Calculate layout
        var layoutItems = TreemapLayout.CalculateLayout(Items, new Rect(0, 0, Bounds.Width, Bounds.Height));

        // Draw treemap rectangles
        foreach (var item in layoutItems)
        {
            DrawTreemapItem(context, item);
        }
    }

    private void DrawTreemapItem(DrawingContext context, TreemapItem item)
    {
        var rect = item.Bounds;
        if (rect.Width < 1 || rect.Height < 1) return;

        // Determine fill color
        var fillBrush = item.Color;
        if (item == _hoveredItem)
        {
            fillBrush = TreemapColors.GetLightColorForCategory(item.Category);
        }
        else if (item == SelectedItem)
        {
            fillBrush = Brushes.LightSkyBlue;
        }

        // Draw rectangle
        context.FillRectangle(fillBrush, rect);
        context.DrawRectangle(new Pen(Brushes.White, 1), rect);

        // Draw text if rectangle is large enough
        if (rect.Width > 30 && rect.Height > 20)
        {
            var text = item.Name;
            if (text.Length > 20)
            {
                text = text.Substring(0, 17) + "...";
            }

            var formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                Math.Max(8, Math.Min(12, rect.Height / 4)),
                Brushes.Black);

            // Center text in rectangle
            var textX = rect.X + (rect.Width - formattedText.Width) / 2;
            var textY = rect.Y + (rect.Height - formattedText.Height) / 2;

            if (textX >= rect.X && textY >= rect.Y &&
                textX + formattedText.Width <= rect.Right &&
                textY + formattedText.Height <= rect.Bottom)
            {
                context.DrawText(formattedText, new Point(textX, textY));
            }
        }

        // Draw size info if rectangle is large enough
        if (rect.Width > 60 && rect.Height > 40)
        {
            var sizeText = FormatFileSize(item.Size);
            var percentText = $"({item.Percentage:F1}%)";

            var sizeFormattedText = new FormattedText(
                sizeText,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                Math.Max(6, Math.Min(10, rect.Height / 6)),
                Brushes.DarkGray);

            var percentFormattedText = new FormattedText(
                percentText,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                Math.Max(6, Math.Min(9, rect.Height / 7)),
                Brushes.DarkGray);

            var sizeX = rect.X + (rect.Width - sizeFormattedText.Width) / 2;
            var sizeY = rect.Y + rect.Height - sizeFormattedText.Height - percentFormattedText.Height - 4;

            var percentX = rect.X + (rect.Width - percentFormattedText.Width) / 2;
            var percentY = rect.Y + rect.Height - percentFormattedText.Height - 2;

            if (sizeX >= rect.X && sizeY >= rect.Y && sizeY > rect.Y + 20)
            {
                context.DrawText(sizeFormattedText, new Point(sizeX, sizeY));
                context.DrawText(percentFormattedText, new Point(percentX, percentY));
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var position = e.GetPosition(this);
        var newHoveredItem = GetItemAt(position);

        if (newHoveredItem != _hoveredItem)
        {
            _hoveredItem = newHoveredItem;
            InvalidateVisual();

            // Update tooltip
            if (_hoveredItem != null)
            {
                ToolTip.SetTip(this, $"{_hoveredItem.Name}\nSize: {FormatFileSize(_hoveredItem.Size)}\nPath: {_hoveredItem.FullPath}");
            }
            else
            {
                ToolTip.SetTip(this, null);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var position = e.GetPosition(this);
        var clickedItem = GetItemAt(position);

        if (clickedItem != null)
        {
            SelectedItem = clickedItem;
            ItemClicked?.Invoke(this, clickedItem);
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_hoveredItem != null)
        {
            _hoveredItem = null;
            InvalidateVisual();
            ToolTip.SetTip(this, null);
        }
    }

    private TreemapItem? GetItemAt(Point position)
    {
        if (Items == null) return null;

        foreach (var item in Items)
        {
            if (item.Bounds.Contains(position))
            {
                return item;
            }
        }

        return null;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:F1} {suffixes[suffixIndex]}";
    }
}
