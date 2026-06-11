using PlugB.Events;

namespace PlugB.Storage;

/// <summary>
/// Contract for a Store-and-Forward storage backend.
/// </summary>
public interface IForwardStore
{
    event EventHandler<BufferOverflowInfo>? BufferOverflow;

    ValueTask EnqueueAsync(ForwardEntry entry, CancellationToken ct);

    IAsyncEnumerable<ForwardEntry> DrainAsync(CancellationToken ct);

    ValueTask<int> CountAsync(CancellationToken ct);

    ValueTask ClearAsync(CancellationToken ct);
}