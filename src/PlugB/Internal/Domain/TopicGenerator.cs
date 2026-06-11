namespace PlugB.Internal.Domain;

internal static class TopicGenerator
{
    public const string Prefix = "spBv1.0";

    public const string MsgTypeNodeBirth = "NBIRTH";
    public const string MsgTypeNodeDeath = "NDEATH";
    public const string MsgTypeNodeData = "NDATA";
    public const string MsgTypeNodeCommand = "NCMD";

    public const string MsgTypeDeviceBirth = "DBIRTH";
    public const string MsgTypeDeviceDeath = "DDEATH";
    public const string MsgTypeDeviceData = "DDATA";
    public const string MsgTypeDeviceCommand = "DCMD";

    public const string MsgTypeState = "STATE";

    /// <summary>
    /// Generates Node-Level Topics: spBv1.0/{groupId}/{messageType}/{edgeNodeId}
    /// </summary>
    public static string GetNodeTopic(string groupId, string edgeNodeId, string messageType)
    {
        return $"{Prefix}/{groupId}/{messageType}/{edgeNodeId}";
    }

    /// <summary>
    /// Generates Device-Level Topics: spBv1.0/{groupId}/{messageType}/{edgeNodeId}/{deviceId}
    /// </summary>
    public static string GetDeviceTopic(string groupId, string edgeNodeId, string deviceId, string messageType)
    {
        return $"{Prefix}/{groupId}/{messageType}/{edgeNodeId}/{deviceId}";
    }

    /// <summary>
    /// Generates the Primary Host STATE topic: spBv1.0/STATE/{hostId} (Rule P1)
    /// </summary>
    public static string GetStateTopic(string hostId)
    {
        return $"{Prefix}/{MsgTypeState}/{hostId}";
    }
}