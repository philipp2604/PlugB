using PlugB.Models;

namespace PlugB.Events;

public record NodeBirthEventArgs(string GroupId, string EdgeNodeId, IReadOnlyDictionary<string, Metric> Metrics);

public record DeviceBirthEventArgs(string GroupId, string EdgeNodeId, string DeviceId, IReadOnlyDictionary<string, Metric> Metrics);

public record DataChangedEventArgs(string GroupId, string EdgeNodeId, string? DeviceId, IReadOnlyCollection<Metric> Metrics);

public record NodeDeathEventArgs(string GroupId, string EdgeNodeId);

public record DeviceDeathEventArgs(string GroupId, string EdgeNodeId, string DeviceId);

public record RebirthRequestedEventArgs(string GroupId, string EdgeNodeId);

public record DecodeFailedEventArgs(string Topic, Exception Error);