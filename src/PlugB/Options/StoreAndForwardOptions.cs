using PlugB.Storage;

namespace PlugB.Options;

/// <summary>
/// Configuration for the Store-and-Forward mechanism.
/// </summary>
public class StoreAndForwardOptions
{
    public bool Enabled { get; set; }
    public int Capacity { get; set; } = 100_000;
    public EvictionPolicy Eviction { get; set; } = EvictionPolicy.DropOldest;

    /// <summary>
    /// The storage implementation. Defaults to an in-memory ring buffer if left null.
    /// </summary>
    public IForwardStore? Store { get; set; }
}