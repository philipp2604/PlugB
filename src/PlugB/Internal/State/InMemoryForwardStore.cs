using System.Runtime.CompilerServices;
using PlugB.Options;

namespace PlugB.Internal.State;

/// <summary>
/// A bounded, thread-safe in-memory FIFO queue for Store-and-Forward.
/// </summary>
internal class InMemoryForwardStore(int capacity, EvictionPolicy eviction) : IForwardStore
{
    private readonly int _capacity = capacity;
    private readonly EvictionPolicy _eviction = eviction;
    private readonly LinkedList<ForwardEntry> _list = new();
    private readonly Lock _lock = new();

    public event EventHandler<BufferOverflowInfo>? BufferOverflow;

    public ValueTask EnqueueAsync(ForwardEntry entry, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_list.Count >= _capacity)
            {
                if (_eviction == EvictionPolicy.RejectNew)
                {
                    throw new InvalidOperationException("Forward store is full (RejectNew policy).");
                }

                if (_eviction == EvictionPolicy.DropOldest)
                {
                    _list.RemoveFirst();
                }
                else if (_eviction == EvictionPolicy.DropNewest)
                {
                    _list.RemoveLast();
                    // just return
                    BufferOverflow?.Invoke(this, new BufferOverflowInfo(1));
                    return ValueTask.CompletedTask;
                }

                BufferOverflow?.Invoke(this, new BufferOverflowInfo(1));
            }
            _list.AddLast(entry);
        }
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<ForwardEntry> DrainAsync([EnumeratorCancellation] CancellationToken ct)
    {
        List<ForwardEntry> batch;
        lock (_lock)
        {
            batch = [.. _list];
            _list.Clear();
        }

        foreach (var item in batch)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    public ValueTask<int> CountAsync(CancellationToken ct)
    {
        lock (_lock) return new ValueTask<int>(_list.Count);
    }

    public ValueTask ClearAsync(CancellationToken ct)
    {
        lock (_lock) _list.Clear();
        return ValueTask.CompletedTask;
    }
}