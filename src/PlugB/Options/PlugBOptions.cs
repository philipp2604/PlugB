using System;
using System.Collections.Generic;
using System.Text;

namespace PlugB.Options;

/// <summary>
/// Configuration options for the PlugB Sparkplug-B Edge Node Client.
/// </summary>
public record PlugBOptions
{
    // broker connection
    public required string BrokerAddress { get; init; }
    public int BrokerPort { get; init; } = 1883;
    public required string ClientId { get; init; }

    // topic elements
    public required string GroupId { get; init; }
    public required string EdgeNodeId { get; init; }

    // optional, might be used later
    public string? PrimaryHostId { get; init; }

    // auth
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool UseTls { get; init; } = false;
}
