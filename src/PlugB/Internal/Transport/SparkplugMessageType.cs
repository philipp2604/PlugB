namespace PlugB.Internal.Transport;

internal enum SparkplugMessageType
{
    NodeBirth,
    NodeData,
    DeviceBirth,
    DeviceData,
    DeviceDeath,
    InternalFlush
}