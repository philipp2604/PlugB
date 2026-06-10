namespace PlugB.Internal.State;

/// <summary>
/// Manages the strict Sparkplug B sequence numbers ('seq' and 'bdSeq').
/// Thread-safe implementation to ensure sequence integrity under all circumstances.
/// </summary>
internal class SequenceManager
{
    private readonly Lock _lock = new();

    // According to the spec, both sequences range from 0 to 255.
    private const ulong MaxSequenceValue = 255;

    private ulong _seq;
    private ulong _bdSeq;

    /// <summary>
    /// Resets the 'seq' counter to 0. 
    /// Must be called strictly BEFORE building the NBIRTH payload.
    /// </summary>
    public void ResetSeq()
    {
        lock (_lock)
        {
            _seq = 0;
        }
    }

    /// <summary>
    /// Retrieves the next 'seq' number and increments the internal counter.
    /// Wraps from 255 back to 0.
    /// </summary>
    public ulong NextSeq()
    {
        lock (_lock)
        {
            ulong current = _seq;
            _seq = current == MaxSequenceValue ? 0 : current + 1;
            return current;
        }
    }

    /// <summary>
    /// Retrieves the next 'bdSeq' number and increments the internal counter.
    /// Wraps from 255 back to 0.
    /// Must be called strictly ONCE per MQTT connection attempt.
    /// </summary>
    public ulong NextBdSeq()
    {
        lock (_lock)
        {
            ulong current = _bdSeq;
            _bdSeq = current == MaxSequenceValue ? 0 : current + 1;
            return current;
        }
    }
}