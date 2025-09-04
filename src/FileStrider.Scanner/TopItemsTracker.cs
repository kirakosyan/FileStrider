using FileStrider.Core.Contracts;

namespace FileStrider.Scanner;

public class TopItemsTracker<T> : ITopItemsTracker<T>
{
    private readonly int _maxItems;
    private readonly IComparer<T> _comparer;
    private readonly List<T> _items = new();
    private readonly object _lock = new();

    public TopItemsTracker(int maxItems, Comparison<T> comparison)
    {
        _maxItems = maxItems;
        _comparer = Comparer<T>.Create(comparison);
    }

    public void Add(T item)
    {
        lock (_lock)
        {
            if (_items.Count < _maxItems)
            {
                _items.Add(item);
                if (_items.Count == _maxItems)
                {
                    _items.Sort(_comparer);
                }
            }
            else
            {
                // Check if item should replace the smallest (last) item
                if (_comparer.Compare(item, _items[_maxItems - 1]) < 0)
                {
                    _items[_maxItems - 1] = item;
                    
                    // Bubble up to maintain sorted order
                    for (int i = _maxItems - 2; i >= 0 && _comparer.Compare(_items[i], _items[i + 1]) > 0; i--)
                    {
                        (_items[i], _items[i + 1]) = (_items[i + 1], _items[i]);
                    }
                }
            }
        }
    }

    public IReadOnlyList<T> GetTop()
    {
        lock (_lock)
        {
            if (_items.Count > 0 && _items.Count == _maxItems)
            {
                return _items.ToList();
            }
            
            // Sort if not yet at capacity
            var sorted = _items.ToList();
            sorted.Sort(_comparer);
            return sorted;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }
    }
}