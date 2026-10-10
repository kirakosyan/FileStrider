using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace FileStrider.MauiApp.ViewModels;

/// <summary>Publishes a whole result set with one collection notification.</summary>
public sealed class ResultCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> values)
    {
        var items = values.ToList();
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
