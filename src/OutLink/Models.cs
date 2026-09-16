using System.Text.Json.Serialization;

namespace OutLink;

/// <summary>A non-negative data transfer allowance in bytes.</summary>
public sealed record DataLimit
{
    public long Bytes { get; init; }
}

/// <summary>Server configuration, including optional fields returned by newer servers.</summary>
public sealed record Server
{
    public string? Name { get; init; }
    public string? ServerId { get; init; }
    public bool MetricsEnabled { get; init; }
    public long CreatedTimestampMs { get; init; }
    public string? Version { get; init; }
    public int PortForNewAccessKeys { get; init; }
    public string? HostnameForAccessKeys { get; init; }
    public DataLimit? AccessKeyDataLimit { get; init; }
}

/// <summary>An access key. Password and AccessUrl contain credentials.</summary>
public sealed class AccessKey
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? Password { get; init; }
    public int Port { get; init; }
    public string? Method { get; init; }
    public string? AccessUrl { get; init; }
    public DataLimit? DataLimit { get; init; }
}

/// <summary>Optional values for key creation; null values let the server choose defaults.</summary>
public sealed class CreateAccessKeyOptions
{
    public string? Name { get; init; }
    public string? Method { get; init; }
    public string? Password { get; init; }
    public int? Port { get; init; }
    public DataLimit? Limit { get; init; }
}

/// <summary>Experimental metrics; this API may change or disappear.</summary>
public sealed record ExperimentalMetrics
{
    public ServerMetrics? Server { get; init; }
    public IReadOnlyList<AccessKeyMetrics> AccessKeys { get; init; } = [];
}

public sealed record ServerMetrics
{
    public TunnelTime? TunnelTime { get; init; }
    public TransferredData? DataTransferred { get; init; }
    public BandwidthMetrics? Bandwidth { get; init; }
    public IReadOnlyList<LocationMetrics> Locations { get; init; } = [];
}

public sealed record TunnelTime
{
    public double Seconds { get; init; }
}

public sealed record TransferredData
{
    public double Bytes { get; init; }
}

public sealed record BandwidthMetrics
{
    public BandwidthSample? Current { get; init; }
    public BandwidthSample? Peak { get; init; }
}

public sealed record BandwidthSample
{
    public TransferredData? Data { get; init; }
    public long Timestamp { get; init; }
}

public sealed record LocationMetrics
{
    public string? Location { get; init; }
    public double? Asn { get; init; }
    public string? AsOrg { get; init; }
    public TunnelTime? TunnelTime { get; init; }
    public TransferredData? DataTransferred { get; init; }
}

public sealed record AccessKeyMetrics
{
    public long AccessKeyId { get; init; }
    public TunnelTime? TunnelTime { get; init; }
    public TransferredData? DataTransferred { get; init; }
    public ConnectionMetrics? Connection { get; init; }
}

public sealed record ConnectionMetrics
{
    public double? LastTrafficSeen { get; init; }
    public DeviceCountSample? PeakDeviceCount { get; init; }
}

public sealed record DeviceCountSample
{
    public long Data { get; init; }
    public long Timestamp { get; init; }
}
