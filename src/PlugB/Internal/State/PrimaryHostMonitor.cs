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

    /// <summary>
    /// Triggered whenever the host state changes (e.g., transitions from Offline to Online).
    /// </summary>
    public event EventHandler<PrimaryHostState>? StateChanged;

    /// <summary>
    /// Gets the current known state of the Primary Host Application.
    /// </summary>
    public PrimaryHostState CurrentState
    {
        get
        {
            lock (_lock) return _state;
        }
    }

    /// <summary>
    /// Processes a newly received STATE message and determines if it constitutes a valid state change.
    /// </summary>
    /// <param name="message">The parsed STATE message.</param>
    /// <returns>True if the state actually changed, False if the message was stale or state remained identical.</returns>
    public bool ProcessStateMessage(StateMessage message)
    {
        PrimaryHostState? newStateToFire = null;

        lock (_lock)
        {
            bool isNewOrValid = false;

            if (!_state.Known)
            {
                isNewOrValid = true;
            }
            else if (message.TimestampMs > _state.LastTimestampMs)
            {
                isNewOrValid = true;
            }
            else if (message.TimestampMs == _state.LastTimestampMs && message.Online)
            {
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

                newStateToFire = _state;
            }
        }

        // Fire event outside the lock to prevent deadlocks in the consuming pipeline
        if (newStateToFire != null)
        {
            StateChanged?.Invoke(this, newStateToFire);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resets the monitor. Typically called upon full disconnection from the broker.
    /// </summary>
    public void Reset()
    {
        PrimaryHostState? newStateToFire = null;

        lock (_lock)
        {
            if (_state.Known)
            {
                _state = new PrimaryHostState(Online: false, LastTimestampMs: 0, Known: false);
                newStateToFire = _state;
            }
        }

        if (newStateToFire != null)
        {
            StateChanged?.Invoke(this, newStateToFire);
        }
    }
}