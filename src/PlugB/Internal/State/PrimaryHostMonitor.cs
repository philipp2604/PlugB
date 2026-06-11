using Microsoft.Extensions.Logging;
using PlugB.Internal.Domain;
using PlugB.Options;

namespace PlugB.Internal.State;

/// <summary>
/// Monitors the Primary Host Application's STATE messages and applies the Sparkplug B staleness rules.
/// </summary>
internal class PrimaryHostMonitor(ILogger? logger)
{
    private readonly ILogger? _logger = logger;
    private PrimaryHostState _state = new(Online: false, LastTimestampMs: 0, Known: false);
    private readonly Lock _lock = new();

    public PrimaryHostState CurrentState
    {
        get
        {
            lock (_lock) return _state;
        }
    }

    /// <summary>
    /// Processes a newly received STATE message and determines if it constitutes a valid state change.
    /// Applies Rule P4 (Timestamp Staleness).
    /// </summary>
    /// <returns>True if the state actually changed, False if the message was stale or state remained identical.</returns>
    public bool ProcessStateMessage(StateMessage message)
    {
        lock (_lock)
        {
            bool isNewOrValid = false;

            // staleness matrix
            if (!_state.Known)
            {
                // no prior state known: accept the flag unconditionally
                isNewOrValid = true;
            }
            else if (message.TimestampMs > _state.LastTimestampMs)
            {
                // timestamp is newer: accept.
                isNewOrValid = true;
            }
            else if (message.TimestampMs == _state.LastTimestampMs && message.Online)
            {
                // timestamp is identical AND status is online: accept. 
                // handles race conditions where Birth overtakes a Will/Death
                isNewOrValid = true;
            }

            if (!isNewOrValid)
            {
                if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
                    _logger?.LogDebug("Discarded stale STATE message. Known TS: {KnownTs}, Received TS: {ReceivedTs}, Online: {Online}",
                        _state.LastTimestampMs, message.TimestampMs, message.Online);
                return false;
            }

            bool statusChanged = _state.Online != message.Online || !_state.Known;

            _state = new PrimaryHostState(message.Online, message.TimestampMs, Known: true);

            if (statusChanged)
            {
                if (_logger?.IsEnabled(LogLevel.Information) ?? false)
                    _logger?.LogInformation("Primary Host state transitioned to: {State} (Timestamp: {Timestamp})",
                        message.Online ? "ONLINE" : "OFFLINE", message.TimestampMs);
            }

            return statusChanged;
        }
    }

    /// <summary>
    /// Resets the monitor. Typically called upon full disconnection from the broker when state becomes unknown again.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _state = new PrimaryHostState(Online: false, LastTimestampMs: 0, Known: false);
        }
    }
}