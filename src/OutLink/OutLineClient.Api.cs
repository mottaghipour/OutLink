namespace OutLink;

public sealed partial class OutLineClient
{
    /// <summary>Gets the server configuration.</summary>
    public Task<Server> GetServerAsync(CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Get, "server", null, OutLinkJsonContext.Default.Server, cancellationToken);

    /// <summary>Changes the hostname or IP address used in access URLs.</summary>
    public Task SetHostnameForAccessKeysAsync(string hostname, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostname);
        return SendEmptyAsync(HttpMethod.Put, "server/hostname-for-access-keys",
            Json(new HostnameRequest(hostname), OutLinkJsonContext.Default.HostnameRequest), cancellationToken);
    }

    /// <summary>Changes the default port for newly created access keys (1–65535).</summary>
    public Task SetPortForNewAccessKeysAsync(int port, CancellationToken cancellationToken = default)
    {
        ValidatePort(port);
        return SendEmptyAsync(HttpMethod.Put, "server/port-for-new-access-keys",
            Json(new PortRequest(port), OutLinkJsonContext.Default.PortRequest), cancellationToken);
    }

    /// <summary>Sets the default data transfer allowance for access keys.</summary>
    public Task SetAccessKeyDataLimitAsync(long bytes, CancellationToken cancellationToken = default) =>
        SetLimitAsync("server/access-key-data-limit", bytes, cancellationToken);

    /// <summary>Removes the default access-key data transfer allowance.</summary>
    public Task RemoveAccessKeyDataLimitAsync(CancellationToken cancellationToken = default) =>
        SendEmptyAsync(HttpMethod.Delete, "server/access-key-data-limit", null, cancellationToken);

    /// <summary>Renames the server.</summary>
    public Task RenameServerAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return SendEmptyAsync(HttpMethod.Put, "name",
            Json(new NameRequest(name), OutLinkJsonContext.Default.NameRequest), cancellationToken);
    }

    /// <summary>Creates an access key, using server defaults for omitted options.</summary>
    public Task<AccessKey> CreateAccessKeyAsync(CreateAccessKeyOptions? options = null,
        CancellationToken cancellationToken = default) => CreateKeyAsync(HttpMethod.Post, "access-keys", options, cancellationToken);

    /// <summary>Creates an access key with a specific identifier.</summary>
    public Task<AccessKey> CreateAccessKeyWithIdAsync(string id, CreateAccessKeyOptions? options = null,
        CancellationToken cancellationToken = default) => CreateKeyAsync(HttpMethod.Put, KeyPath(id), options, cancellationToken);

    /// <summary>Lists the server's access keys.</summary>
    public async Task<IReadOnlyList<AccessKey>> GetAccessKeysAsync(CancellationToken cancellationToken = default) =>
        (await SendJsonAsync(HttpMethod.Get, "access-keys", null,
            OutLinkJsonContext.Default.AccessKeyList, cancellationToken).ConfigureAwait(false)).AccessKeys;

    /// <summary>Gets an access key by identifier.</summary>
    public Task<AccessKey> GetAccessKeyAsync(string id, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Get, KeyPath(id), null, OutLinkJsonContext.Default.AccessKey, cancellationToken);

    /// <summary>Deletes an access key.</summary>
    public Task DeleteAccessKeyAsync(string id, CancellationToken cancellationToken = default) =>
        SendEmptyAsync(HttpMethod.Delete, KeyPath(id), null, cancellationToken);

    /// <summary>Renames an access key. An empty name clears its label.</summary>
    public Task RenameAccessKeyAsync(string id, string name, CancellationToken cancellationToken = default)
    {
        var path = KeyPath(id) + "/name";
        ArgumentNullException.ThrowIfNull(name);
        return SendEmptyAsync(HttpMethod.Put, path,
            Json(new NameRequest(name), OutLinkJsonContext.Default.NameRequest), cancellationToken);
    }

    /// <summary>Sets an individual access key's data transfer allowance.</summary>
    public Task SetAccessKeyDataLimitAsync(string id, long bytes, CancellationToken cancellationToken = default) =>
        SetLimitAsync(KeyPath(id) + "/data-limit", bytes, cancellationToken);

    /// <summary>Removes an individual access key's override; the server default still applies.</summary>
    public Task RemoveAccessKeyDataLimitAsync(string id, CancellationToken cancellationToken = default) =>
        SendEmptyAsync(HttpMethod.Delete, KeyPath(id) + "/data-limit", null, cancellationToken);

    /// <summary>Gets bytes transferred by access-key identifier.</summary>
    public async Task<IReadOnlyDictionary<string, long>> GetTransferMetricsAsync(CancellationToken cancellationToken = default) =>
        (await SendJsonAsync(HttpMethod.Get, "metrics/transfer", null,
            OutLinkJsonContext.Default.TransferMetrics, cancellationToken).ConfigureAwait(false)).BytesTransferredByUserId;

    /// <summary>Gets whether anonymous metrics sharing is enabled.</summary>
    public async Task<bool> GetMetricsEnabledAsync(CancellationToken cancellationToken = default) =>
        (await SendJsonAsync(HttpMethod.Get, "metrics/enabled", null,
            OutLinkJsonContext.Default.MetricsSetting, cancellationToken).ConfigureAwait(false)).MetricsEnabled;

    /// <summary>Enables or disables anonymous metrics sharing.</summary>
    public Task SetMetricsEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        SendEmptyAsync(HttpMethod.Put, "metrics/enabled",
            Json(new MetricsSetting(enabled), OutLinkJsonContext.Default.MetricsSetting), cancellationToken);

    /// <summary>Gets experimental metrics, optionally for a time range such as "24h". This API is unstable.</summary>
    public Task<ExperimentalMetrics> GetExperimentalServerMetricsAsync(string? since = null,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Get, "experimental/server/metrics" +
            (since is null ? "" : "?since=" + Uri.EscapeDataString(since)), null,
            OutLinkJsonContext.Default.ExperimentalMetrics, cancellationToken);

    /// <summary>Calls the legacy data-limit route, supporting its redirect to the stable route.</summary>
    [Obsolete("Use SetAccessKeyDataLimitAsync instead.")]
    public Task SetExperimentalAccessKeyDataLimitAsync(long bytes, CancellationToken cancellationToken = default) =>
        SetLimitAsync("experimental/access-key-data-limit", bytes, cancellationToken, "server/access-key-data-limit");

    /// <summary>Calls the legacy data-limit removal route, supporting its redirect to the stable route.</summary>
    [Obsolete("Use RemoveAccessKeyDataLimitAsync instead.")]
    public Task RemoveExperimentalAccessKeyDataLimitAsync(CancellationToken cancellationToken = default) =>
        SendEmptyAsync(HttpMethod.Delete, "experimental/access-key-data-limit", null, cancellationToken, "server/access-key-data-limit");

    private Task SetLimitAsync(string path, long bytes, CancellationToken cancellationToken, string? redirectPath = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        return SendEmptyAsync(HttpMethod.Put, path,
            Json(new LimitRequest(new DataLimit { Bytes = bytes }), OutLinkJsonContext.Default.LimitRequest),
            cancellationToken, redirectPath);
    }

    private Task<AccessKey> CreateKeyAsync(HttpMethod method, string path, CreateAccessKeyOptions? options,
        CancellationToken cancellationToken)
    {
        if (options?.Port is { } port) ValidatePort(port);
        if (options?.Limit is { } limit) ArgumentOutOfRangeException.ThrowIfNegative(limit.Bytes, nameof(options));
        return SendJsonAsync(method, path,
            options is null ? null : Json(options, OutLinkJsonContext.Default.CreateAccessKeyOptions),
            OutLinkJsonContext.Default.AccessKey, cancellationToken);
    }

    private static void ValidatePort(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
    }

    private static string KeyPath(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        // URI processing normalizes dot segments even when escaped.
        if (id is "." or "..") throw new ArgumentException("An access-key identifier cannot be a dot segment.", nameof(id));
        return "access-keys/" + Uri.EscapeDataString(id);
    }
}
