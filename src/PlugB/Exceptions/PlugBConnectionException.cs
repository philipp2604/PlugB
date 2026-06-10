namespace PlugB.Exceptions;

/// <summary>
/// Thrown when a connection to the MQTT broker fails or is interrupted.
/// </summary>
public class PlugBConnectionException(string message, Exception innerException) : Exception(message, innerException)
{
}