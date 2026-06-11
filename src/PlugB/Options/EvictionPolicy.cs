namespace PlugB.Options;

/// <summary>
/// Defines the behavior when the Store-and-Forward buffer reaches its maximum capacity.
/// </summary>
public enum EvictionPolicy
{
    /// <summary>
    /// Discards the oldest historical data to make room for new data.
    /// </summary>
    DropOldest,

    /// <summary>
    /// Discards the newly arriving data and keeps the oldest historical data.
    /// </summary>
    DropNewest,

    /// <summary>
    /// Throws an exception on the publishing thread when the buffer is full.
    /// </summary>
    RejectNew
}