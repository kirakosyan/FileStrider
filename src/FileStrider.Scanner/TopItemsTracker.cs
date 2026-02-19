using FileStrider.Core.Contracts;

namespace FileStrider.Scanner;

/// <summary>
/// Thread-safe implementation of a top-N items tracker that efficiently maintains 
/// the best N items according to a specified comparison function.
/// </summary>
/// <typeparam name="T">The type of items to track.</typeparam>
public class TopItemsTracker<T> : ITopItemsTracker<T>, IDisposable
{
    private readonly int _maxItems;
    private readonly IComparer<T> _comparer;
    private readonly List<T> _items = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TopItemsTracker{T}"/> class.
    /// </summary>
    /// <param name="maxItems">The maximum number of items to track.</param>
    /// <param name="comparison">The comparison function to determine which items are "best".</param>
    public TopItemsTracker(int maxItems, Comparison<T> comparison)
    {
        if (maxItems <= 0) throw new ArgumentOutOfRangeException(nameof(maxItems), "maxItems must be greater than 0");
        ArgumentNullException.ThrowIfNull(comparison);

        _maxItems = maxItems;
        _comparer = Comparer<T>.Create(comparison);
    }

    /// <summary>
    /// Adds an item to the tracker. If the item should be in the top N, it will be stored and maintained in sorted order.
    /// This method is thread-safe and can be called concurrently from multiple threads.
    /// </summary>
    /// <param name="item">The item to add to the tracker.</param>
    public void Add(T item)
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);

        if (item == null) return; // Ignore null items

        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);

            if (_items.Count < _maxItems)
            {
                _items.Add(item);
                _items.Sort(_comparer);
            }
            else
            {
                // Check if item should replace the smallest (last) item
                if (_comparer.Compare(item, _items[_maxItems - 1]) < 0)
                {
                    _items[_maxItems - 1] = item;

                    // Bubble up to maintain sorted order (simpler and more reliable than binary search insertion)
                    for (int i = _maxItems - 2; i >= 0 && _comparer.Compare(_items[i], _items[i + 1]) > 0; i--)
                    {
                        (_items[i], _items[i + 1]) = (_items[i + 1], _items[i]);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets the current top items in order (best to worst according to the comparison function).
    /// This method is thread-safe.
    /// </summary>
    /// <returns>A read-only list of the top items currently being tracked.</returns>
    public IReadOnlyList<T> GetTop()
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);

        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);

            return _items.ToList();
        }
    }

    /// <summary>
    /// Clears all tracked items from the tracker.
    /// This method is thread-safe.
    /// </summary>
    public void Clear()
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);

        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
            _items.Clear();
        }
    }

    /// <summary>
    /// Disposes the tracker and releases associated resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose method for proper disposal pattern.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            lock (_lock)
            {
                _items.Clear();
                _disposed = true;
            }
        }
    }
}