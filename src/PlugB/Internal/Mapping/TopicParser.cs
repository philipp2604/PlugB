namespace PlugB.Internal.Mapping;

internal static class TopicParser
{
    /// <summary>
    /// Parses an incoming MQTT topic string into a structured Sparkplug B topic format.
    /// Returns null if the topic does not match the valid Sparkplug B v1.0 structure.
    /// </summary>
    public static ParsedTopic? Parse(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return null;
        }

        var parts = topic.Split('/');

        if (parts.Length < 3 || parts[0] != TopicGenerator.Prefix)
        {
            return null;
        }

        // Check if STATE topic: spBv1.0/STATE/{hostId}
        if (parts[1] == TopicGenerator.MsgTypeState)
        {
            if (parts.Length == 3)
            {
                return new ParsedTopic(SparkplugMessageKind.State, null, null, null, parts[2]);
            }
            return null; // Invalid STATE structure
        }

        // Node or Device topic: spBv1.0/{groupId}/{messageType}/{edgeNodeId}/[deviceId]
        if (parts.Length < 4 || parts.Length > 5)
        {
            return null;
        }

        var groupId = parts[1];
        var msgTypeString = parts[2];
        var edgeNodeId = parts[3];
        var deviceId = parts.Length == 5 ? parts[4] : null;

        var kind = ParseKind(msgTypeString);

        return new ParsedTopic(kind, groupId, edgeNodeId, deviceId, null);
    }

    private static SparkplugMessageKind ParseKind(string messageType) => messageType switch
    {
        TopicGenerator.MsgTypeNodeBirth => SparkplugMessageKind.NBirth,
        TopicGenerator.MsgTypeNodeDeath => SparkplugMessageKind.NDeath,
        TopicGenerator.MsgTypeNodeData => SparkplugMessageKind.NData,
        TopicGenerator.MsgTypeNodeCommand => SparkplugMessageKind.NCmd,
        TopicGenerator.MsgTypeDeviceBirth => SparkplugMessageKind.DBirth,
        TopicGenerator.MsgTypeDeviceDeath => SparkplugMessageKind.DDeath,
        TopicGenerator.MsgTypeDeviceData => SparkplugMessageKind.DData,
        TopicGenerator.MsgTypeDeviceCommand => SparkplugMessageKind.DCmd,
        _ => SparkplugMessageKind.Unknown
    };
}