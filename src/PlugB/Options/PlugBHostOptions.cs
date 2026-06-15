namespace PlugB.Options;

/// <summary>
/// Configuration options for the PlugB Sparkplug-B Host Application.
/// </summary>
public record PlugBHostOptions
{
    public required IReadOnlyList<MqttBroker> Servers { get; init; }
    public required string HostId { get; init; }
    public string ClientId { get; init; } = Guid.NewGuid().ToString();
    public IReadOnlyList<string> GroupFilters { get; init; } = [];
    public bool RebirthOnGap { get; init; } = true;
    public string? Username { get; init; }
    public string? Password { get; init; }
}