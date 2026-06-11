using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace PlugB.Internal.Domain;

internal static class StateParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private class StateDto
    {
        [JsonPropertyName("online")]
        public bool? Online { get; set; }

        [JsonPropertyName("timestamp")]
        public long? Timestamp { get; set; }
    }

    /// <summary>
    /// Decodes a STATE message payload from JSON (Rule P1).
    /// Returns null if the JSON is invalid or missing required fields.
    /// </summary>
    public static StateMessage? Parse(string payload, ILogger? logger)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<StateDto>(payload, JsonOptions);

            if (dto == null || !dto.Online.HasValue || !dto.Timestamp.HasValue)
            {
                logger?.LogWarning("Received invalid STATE payload. Missing required fields 'online' or 'timestamp'. Payload: {Payload}", payload);
                return null;
            }

            return new StateMessage(dto.Online.Value, dto.Timestamp.Value);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Failed to parse STATE payload as JSON. Payload: {Payload}", payload);
            return null;
        }
    }
}