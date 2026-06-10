namespace PlugB.Exceptions;

/// <summary>
/// Thrown when an operation is attempted while the Sparkplug B client is not connected to the MQTT broker.
/// </summary>
public class PlugBNotConnectedException : Exception
{
    public PlugBNotConnectedException()
        : base("The PlugB client is currently not connected to the MQTT broker. Cannot publish data.")
    {
    }

    public PlugBNotConnectedException(string message) : base(message)
    {
    }
}