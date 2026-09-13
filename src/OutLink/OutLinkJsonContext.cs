using System.Text.Json.Serialization;

namespace OutLink;

internal sealed record NameRequest(string Name);
internal sealed record HostnameRequest(string Hostname);
internal sealed record PortRequest(int Port);
internal sealed record LimitRequest(DataLimit Limit);
internal sealed record MetricsSetting(bool MetricsEnabled);
internal sealed record AccessKeyList
{
    public required IReadOnlyList<AccessKey> AccessKeys { get; init; }
}
internal sealed record TransferMetrics
{
    public required IReadOnlyDictionary<string, long> BytesTransferredByUserId { get; init; }
}
internal sealed record ApiError(string? Code, string? Message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Server))]
[JsonSerializable(typeof(AccessKey))]
[JsonSerializable(typeof(CreateAccessKeyOptions))]
[JsonSerializable(typeof(ExperimentalMetrics))]
[JsonSerializable(typeof(NameRequest))]
[JsonSerializable(typeof(HostnameRequest))]
[JsonSerializable(typeof(PortRequest))]
[JsonSerializable(typeof(LimitRequest))]
[JsonSerializable(typeof(MetricsSetting))]
[JsonSerializable(typeof(AccessKeyList))]
[JsonSerializable(typeof(TransferMetrics))]
[JsonSerializable(typeof(ApiError))]
internal partial class OutLinkJsonContext : JsonSerializerContext;
