using System.Collections.Concurrent;

namespace PlugB.Internal.State;

internal enum SequenceValidationResult
{
    Valid,
    DataBeforeBirth,
    SequenceGap,
    InvalidDeathBdSeq
}

/// <summary>
/// Tracks sequence numbers (seq) and birth/death correlation (bdSeq) for all connected Edge Nodes.
/// Enforces Sparkplug B sequence rules and signals when a Rebirth is required.
/// </summary>
internal class HostSequenceTracker
{
    private class NodeState
    {
        public ulong ExpectedSeq { get; set; }
        public ulong BdSeq { get; set; }
        public bool BirthReceived { get; set; }
    }

    private readonly ConcurrentDictionary<string, NodeState> _nodes = new();
    private readonly Lock _lock = new();

    private static string GetKey(string groupId, string edgeNodeId) => $"{groupId}/{edgeNodeId}";

    public void ProcessNBirth(string groupId, string edgeNodeId, ulong bdSeq)
    {
        var key = GetKey(groupId, edgeNodeId);
        var state = _nodes.GetOrAdd(key, _ => new NodeState());

        lock (_lock)
        {
            state.ExpectedSeq = 1; // NBIRTH is seq 0, next expected is 1
            state.BdSeq = bdSeq;
            state.BirthReceived = true;
        }
    }

    public SequenceValidationResult ValidateSequence(string groupId, string edgeNodeId, ulong seq)
    {
        var key = GetKey(groupId, edgeNodeId);
        if (!_nodes.TryGetValue(key, out var state))
        {
            return SequenceValidationResult.DataBeforeBirth;
        }

        lock (_lock)
        {
            if (!state.BirthReceived)
            {
                return SequenceValidationResult.DataBeforeBirth;
            }

            if (seq != state.ExpectedSeq)
            {
                state.BirthReceived = false; // Suspend processing until new NBIRTH
                return SequenceValidationResult.SequenceGap;
            }

            state.ExpectedSeq = (state.ExpectedSeq + 1) % 256;
            return SequenceValidationResult.Valid;
        }
    }

    public SequenceValidationResult ValidateNDeath(string groupId, string edgeNodeId, ulong bdSeq)
    {
        var key = GetKey(groupId, edgeNodeId);
        if (!_nodes.TryGetValue(key, out var state))
        {
            return SequenceValidationResult.InvalidDeathBdSeq;
        }

        lock (_lock)
        {
            if (bdSeq != state.BdSeq)
            {
                return SequenceValidationResult.InvalidDeathBdSeq; // Stale LWT
            }

            state.BirthReceived = false;
            return SequenceValidationResult.Valid;
        }
    }

    public void MarkUnborn(string groupId, string edgeNodeId)
    {
        var key = GetKey(groupId, edgeNodeId);
        if (_nodes.TryGetValue(key, out var state))
        {
            lock (_lock)
            {
                state.BirthReceived = false;
            }
        }
    }
}