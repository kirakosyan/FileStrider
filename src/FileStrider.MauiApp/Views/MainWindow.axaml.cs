using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileStrider.Core.Models;
using FileStrider.MauiApp.ViewModels;

namespace FileStrider.MauiApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, ResultList_KeyDown, RoutingStrategies.Tunnel);
    }

    private async void Treemap_ItemClicked(object? sender, FileStrider.MauiApp.Models.TreemapItem item)
    {
        if (DataContext is MainWindowViewModel model)
            await model.ActivateTreemapItemCommand.ExecuteAsync(item);
    }

    private void Treemap_NavigateUpRequested(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel model) model.NavigateUpCommand.Execute(null);
    }

    private void Breadcrumb_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path } && DataContext is MainWindowViewModel model)
            model.NavigateTreemap(path);
    }

    private async void ExplorerList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: ExplorerRow row } && DataContext is MainWindowViewModel model)
            await model.ExploreItemCommand.ExecuteAsync(row);
    }

    private void ResultList_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not ListBox list || !e.TryGetPosition(list, out var point)) return;
        // Right-click acts on the row under the pointer, not a previously selected row.
        var row = list.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault(item =>
            item.TranslatePoint(default, list) is { } origin && new Avalonia.Rect(origin, item.Bounds.Size).Contains(point));
        if (row is not null) list.SelectedItem = row.DataContext;
    }

    private async void ResultList_KeyDown(object? sender, KeyEventArgs e)
    {
        var list = e.Source as ListBox ??
            (e.Source as Control)?.GetVisualAncestors().OfType<ListBox>().FirstOrDefault();
        if (list is null || DataContext is not MainWindowViewModel model) return;
        if (e.Key == Key.Back && ReferenceEquals(list.ItemsSource, model.ExplorerItems))
        {
            model.NavigateUpCommand.Execute(null);
            e.Handled = true;
            return;
        }
        var path = list.SelectedItem switch
        {
            FileItem file => file.FullPath,
            FolderItem folder => folder.FullPath,
            ExplorerRow row => row.FullPath,
            InsightRow insight => insight.FullPath,
            CoverageRow coverage => coverage.FullPath,
            _ => null
        };
        if (path is null) return;
        if (e.Key == Key.C && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            e.Handled = true;
            await model.CopyPathCommand.ExecuteAsync(path);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (list.SelectedItem is ExplorerRow row) await model.ExploreItemCommand.ExecuteAsync(row);
            else await model.OpenPathCommand.ExecuteAsync(path);
        }
    }

    private async void TopFilesList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is FileItem fileItem)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.OpenFileLocationCommand.ExecuteAsync(fileItem);
                e.Handled = true;
            }
        }
    }

    private async void TopFoldersList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is FolderItem folderItem)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.OpenFolderLocationCommand.ExecuteAsync(folderItem);
                e.Handled = true;
            }
        }
    }
}
