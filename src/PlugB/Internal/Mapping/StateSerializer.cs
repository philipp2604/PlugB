using System.Text.Json;

namespace PlugB.Internal.Mapping;

internal static class StateSerializer
{
    /// <summary>
    /// Serializes the Primary Host STATE payload according to the Sparkplug B spec.
    /// </summary>
    public static string Serialize(bool online, long timestamp)
    {
        var dto = new
        {
            online,
            timestamp
        };

        return JsonSerializer.Serialize(dto);
    }
}