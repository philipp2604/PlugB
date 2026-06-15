namespace PlugB.Internal.Mapping;

internal record ParsedTopic(
    SparkplugMessageKind Kind,
    string? GroupId,
    string? EdgeNodeId,
    string? DeviceId,
    string? HostId);