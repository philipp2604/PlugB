using PlugB.Models;

namespace PlugB.Internal.Transport;

/// <summary>
/// Encapsulates the high-level connection state of the Edge Node and ensures thread-safe transitions.
/// </summary>
internal class ConnectionStateMachine
{
    private PlugBConnectionState _currentState = PlugBConnectionState.Disconnected;
    private readonly Lock _lock = new();

    /// <summary>
    /// Triggered whenever the internal state transitions to a new value.
    /// </summary>
    public event EventHandler<PlugBConnectionState>? StateChanged;

    /// <summary>
    /// Gets the current state of the machine.
    /// </summary>
    public PlugBConnectionState CurrentState
    {
        get { lock (_lock) return _currentState; }
    }

    /// <summary>
    /// Safely transitions the state machine to a new state and fires the corresponding event.
    /// </summary>
    /// <param name="newState">The target state.</param>
    public void TransitionTo(PlugBConnectionState newState)
    {
        lock (_lock)
        {
            if (_currentState == newState) return;
            _currentState = newState;
        }

        StateChanged?.Invoke(this, newState);
    }
}