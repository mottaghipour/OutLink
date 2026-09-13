using System.Net;
using System.Text;
using System.Text.Json;
using Client = global::OutLink.OutLink;

namespace OutLink.Tests;

public class ApiTests
{
    public static TheoryData<string, string, string, string?, string?> Operations => new()
    {
        { "server", "GET", "server", null, "{}" },
        { "hostname", "PUT", "server/hostname-for-access-keys", "{\"hostname\":\"vpn.example.com\"}", null },
        { "port", "PUT", "server/port-for-new-access-keys", "{\"port\":65535}", null },
        { "default-limit", "PUT", "server/access-key-data-limit", "{\"limit\":{\"bytes\":8589934592}}", null },
        { "remove-default-limit", "DELETE", "server/access-key-data-limit", null, null },
        { "experimental-metrics", "GET", "experimental/server/metrics?since=24h", null, "{}" },
        { "rename-server", "PUT", "name", "{\"name\":\"My server\"}", null },
        { "create", "POST", "access-keys", null, "{\"id\":\"key\"}" },
        { "list", "GET", "access-keys", null, "{\"accessKeys\":[]}" },
        { "create-id", "PUT", "access-keys/key", null, "{\"id\":\"key\"}" },
        { "get", "GET", "access-keys/key", null, "{\"id\":\"key\"}" },
        { "delete", "DELETE", "access-keys/key", null, null },
        { "rename-key", "PUT", "access-keys/key/name", "{\"name\":\"\"}", null },
        { "key-limit", "PUT", "access-keys/key/data-limit", "{\"limit\":{\"bytes\":0}}", null },
        { "remove-key-limit", "DELETE", "access-keys/key/data-limit", null, null },
        { "transfer", "GET", "metrics/transfer", null, "{\"bytesTransferredByUserId\":{}}" },
        { "metrics-enabled", "GET", "metrics/enabled", null, "{\"metricsEnabled\":true}" },
        { "set-metrics", "PUT", "metrics/enabled", "{\"metricsEnabled\":false}", null },
        { "legacy-limit", "PUT", "experimental/access-key-data-limit", "{\"limit\":{\"bytes\":1}}", null },
        { "legacy-remove", "DELETE", "experimental/access-key-data-limit", null, null }
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Operations_SendDocumentedMethodPathAndBody(string operation, string method, string path,
        string? expectedBody, string? responseBody)
    {
        var calls = 0;
        using var client = Create(async (request, token) =>
        {
            calls++;
            Assert.Equal(method, request.Method.Method);
            Assert.Equal("https://example.com/secret/" + path, request.RequestUri!.AbsoluteUri);
            if (expectedBody is null) Assert.Null(request.Content);
            else
            {
                Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
                AssertJson(expectedBody, await request.Content.ReadAsStringAsync(token));
            }
            return Response(responseBody, responseBody is null ? HttpStatusCode.NoContent :
                operation is "create" or "create-id" ? HttpStatusCode.Created : HttpStatusCode.OK);
        });

        await Invoke(client, operation);

        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_SerializesAllOptions(bool withId)
    {
        using var client = Create(async (request, token) =>
        {
            AssertJson("""{"name":"نام","method":"aes-192-gcm","password":"p\"&","port":12345,"limit":{"bytes":8589934592}}""",
                await request.Content!.ReadAsStringAsync(token));
            return Response("""{"id":"key"}""", HttpStatusCode.Created);
        });
        var options = new CreateAccessKeyOptions
        {
            Name = "نام", Method = "aes-192-gcm", Password = "p\"&", Port = 12345,
            Limit = new DataLimit { Bytes = 8589934592 }
        };
        var key = withId ? await client.CreateAccessKeyWithIdAsync("key", options) : await client.CreateAccessKeyAsync(options);
        Assert.Equal("key", key.Id);
    }

    [Fact]
    public async Task Create_OmitsUnsetOptions()
    {
        using var client = Create(async (request, token) =>
        {
            AssertJson("{}", await request.Content!.ReadAsStringAsync(token));
            return Response("""{"id":"key"}""");
        });
        await client.CreateAccessKeyAsync(new CreateAccessKeyOptions());
    }

    [Fact]
    public async Task GetServer_DeserializesConfigurationAndOptionalFields()
    {
        using var client = Create((_, _) => Task.FromResult(Response("""
            {"name":"Server","serverId":"id","metricsEnabled":true,"createdTimestampMs":1536613192052,
             "version":"1.0.0","portForNewAccessKeys":1234,"hostnameForAccessKeys":"vpn.example.com",
             "accessKeyDataLimit":{"bytes":8589934592},"futureField":true}
            """)));
        var server = await client.GetServerAsync();
        Assert.Equal("Server", server.Name);
        Assert.Equal("id", server.ServerId);
        Assert.True(server.MetricsEnabled);
        Assert.Equal(1536613192052, server.CreatedTimestampMs);
        Assert.Equal("1.0.0", server.Version);
        Assert.Equal(1234, server.PortForNewAccessKeys);
        Assert.Equal("vpn.example.com", server.HostnameForAccessKeys);
        Assert.Equal(8589934592, server.AccessKeyDataLimit!.Bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetKeys_DeserializesCredentialsAndOptionalLimits(bool list)
    {
        const string json = """{"id":"key","name":"Name","password":"password","port":1234,"method":"aes-192-gcm","accessUrl":"ss://value","dataLimit":{"bytes":8589934592}}""";
        using var client = Create((_, _) => Task.FromResult(Response(list ? "{\"accessKeys\":[" + json + "]}" : json)));
        var key = list ? Assert.Single(await client.GetAccessKeysAsync()) : await client.GetAccessKeyAsync("key");
        Assert.Equal("key", key.Id);
        Assert.Equal("Name", key.Name);
        Assert.Equal("password", key.Password);
        Assert.Equal(1234, key.Port);
        Assert.Equal("aes-192-gcm", key.Method);
        Assert.Equal("ss://value", key.AccessUrl);
        Assert.Equal(8589934592, key.DataLimit!.Bytes);
    }

    [Fact]
    public async Task GetTransferMetrics_PreservesLargeCountersAndStringIds()
    {
        using var client = Create((_, _) => Task.FromResult(Response("""{"bytesTransferredByUserId":{"custom-id":5958113497}}""")));
        Assert.Equal(5958113497, (await client.GetTransferMetricsAsync())["custom-id"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetMetricsEnabled_ReturnsSetting(bool enabled)
    {
        using var client = Create((_, _) => Task.FromResult(Response("{\"metricsEnabled\":" + (enabled ? "true" : "false") + "}")));
        Assert.Equal(enabled, await client.GetMetricsEnabledAsync());
    }

    [Fact]
    public async Task ExperimentalMetrics_DeserializesAllNestedFieldsAndNullLocationMetadata()
    {
        using var client = Create((_, _) => Task.FromResult(Response("""
            {"server":{"tunnelTime":{"seconds":100.5},"dataTransferred":{"bytes":100},
            "bandwidth":{"current":{"data":{"bytes":10},"timestamp":1739284734},"peak":{"data":{"bytes":80},"timestamp":1738959398}},
            "locations":[{"location":"US","asn":null,"asOrg":null,"dataTransferred":{"bytes":100},"tunnelTime":{"seconds":100}}]},
            "accessKeys":[{"accessKeyId":0,"tunnelTime":{"seconds":100},"dataTransferred":{"bytes":100},
            "connection":{"lastTrafficSeen":1739284734,"peakDeviceCount":{"data":4,"timestamp":1738959398}}}]}
            """)));
        var metrics = await client.GetExperimentalServerMetricsAsync();
        Assert.Equal(100.5, metrics.Server!.TunnelTime!.Seconds);
        Assert.Equal(100, metrics.Server.DataTransferred!.Bytes);
        Assert.Equal(10, metrics.Server.Bandwidth!.Current!.Data!.Bytes);
        Assert.Equal(1739284734, metrics.Server.Bandwidth.Current.Timestamp);
        Assert.Equal(80, metrics.Server.Bandwidth.Peak!.Data!.Bytes);
        Assert.Equal(1738959398, metrics.Server.Bandwidth.Peak.Timestamp);
        var location = Assert.Single(metrics.Server.Locations);
        Assert.Equal("US", location.Location);
        Assert.Null(location.Asn);
        Assert.Null(location.AsOrg);
        Assert.Equal(100, location.TunnelTime!.Seconds);
        Assert.Equal(100, location.DataTransferred!.Bytes);
        var key = Assert.Single(metrics.AccessKeys);
        Assert.Equal(0, key.AccessKeyId);
        Assert.Equal(100, key.TunnelTime!.Seconds);
        Assert.Equal(100, key.DataTransferred!.Bytes);
        Assert.Equal(1739284734, key.Connection!.LastTrafficSeen);
        Assert.Equal(4, key.Connection.PeakDeviceCount!.Data);
        Assert.Equal(1738959398, key.Connection.PeakDeviceCount.Timestamp);
    }

    [Theory]
    [InlineData(null, "experimental/server/metrics")]
    [InlineData("1h&other=x +", "experimental/server/metrics?since=1h%26other%3Dx%20%2B")]
    public async Task ExperimentalMetrics_EncodesOptionalQuery(string? since, string path)
    {
        using var client = Create((request, _) =>
        {
            Assert.Equal("https://example.com/secret/" + path, request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Response("{}"));
        });
        await client.GetExperimentalServerMetricsAsync(since);
    }

    [Fact]
    public async Task KeyIdentifiers_AreEncodedAsSinglePathSegments()
    {
        using var client = Create((request, _) =>
        {
            Assert.Equal("https://example.com/secret/access-keys/a%2Fb%3F%23%20%25", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Response("""{"id":"a/b?# %"}"""));
        });
        await client.GetAccessKeyAsync("a/b?# %");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(500)]
    public async Task ApiErrors_PreserveStatusAndStructuredDetails(int status)
    {
        const string body = """{"code":"TestError","message":"Server explanation"}""";
        using var client = Create((_, _) => Task.FromResult(Response(body, (HttpStatusCode)status)));
        var error = await Assert.ThrowsAsync<OutLinkApiException>(() => client.GetServerAsync());
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Equal("TestError", error.Code);
        Assert.Equal("Server explanation", error.ApiMessage);
        Assert.Equal(body, error.ResponseBody);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>Bad gateway</html>")]
    [InlineData("null")]
    [InlineData("{\"code\":123}")]
    public async Task NonJsonErrors_PreserveStatusAndBody(string body)
    {
        using var client = Create((_, _) => Task.FromResult(Response(body, HttpStatusCode.BadGateway)));
        var error = await Assert.ThrowsAsync<OutLinkApiException>(() => client.DeleteAccessKeyAsync("key"));
        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.Equal(body, error.ResponseBody);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("invalid json")]
    [InlineData("[]")]
    public async Task InvalidSuccessBody_ThrowsJsonException(string body)
    {
        using var client = Create((_, _) => Task.FromResult(Response(body)));
        await Assert.ThrowsAsync<JsonException>(() => client.GetServerAsync());
    }

    [Fact]
    public async Task MissingRequiredKeyId_ThrowsJsonException()
    {
        using var client = Create((_, _) => Task.FromResult(Response("{}")));
        await Assert.ThrowsAsync<JsonException>(() => client.GetAccessKeyAsync("key"));
    }

    [Theory]
    [InlineData("legacy-limit")]
    [InlineData("legacy-remove")]
    public async Task LegacyRedirect_UsesKnownStableRouteAndPreservesBody(string operation)
    {
        var calls = 0;
        using var client = Create(async (request, token) =>
        {
            calls++;
            Assert.Equal("https://example.com/secret/" + (calls == 1 ? "experimental" : "server") + "/access-key-data-limit",
                request.RequestUri!.AbsoluteUri);
            Assert.Equal(operation == "legacy-limit" ? HttpMethod.Put : HttpMethod.Delete, request.Method);
            if (operation == "legacy-limit") AssertJson("{\"limit\":{\"bytes\":1}}", await request.Content!.ReadAsStringAsync(token));
            var response = Response(null, calls == 1 ? HttpStatusCode.PermanentRedirect : HttpStatusCode.NoContent);
            response.Headers.Location = new Uri("https://untrusted.example/steal");
            return response;
        });
        await Invoke(client, operation);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task StableRoute_DoesNotFollowRedirectsOrRetryFailures()
    {
        var calls = 0;
        using var client = Create((_, _) =>
        {
            calls++;
            return Task.FromResult(Response(null, HttpStatusCode.PermanentRedirect));
        });
        await Assert.ThrowsAsync<OutLinkApiException>(() => client.CreateAccessKeyAsync());
        Assert.Equal(1, calls);
    }

    public static IEnumerable<object[]> OperationNames => Operations.Select(row => new object[] { row[0] });

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task EveryOperation_PropagatesCancellation(string operation)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = Create(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Response(null);
        });
        using var cancellation = new CancellationTokenSource();
        var task = Invoke(client, operation, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task TransportErrors_ArePropagatedWithoutRetry()
    {
        var calls = 0;
        var expected = new HttpRequestException("Connection failed");
        using var client = Create((_, _) => { calls++; throw expected; });
        Assert.Same(expected, await Assert.ThrowsAsync<HttpRequestException>(() => client.GetServerAsync()));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task InvalidPorts_AreRejectedBeforeSending(int port)
    {
        using var client = NoRequests();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.SetPortForNewAccessKeysAsync(port));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.CreateAccessKeyAsync(new() { Port = port }));
    }

    [Fact]
    public async Task NegativeLimits_AreRejectedBeforeSending()
    {
        using var client = NoRequests();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.SetAccessKeyDataLimitAsync(-1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.SetAccessKeyDataLimitAsync("key", -1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.CreateAccessKeyAsync(new() { Limit = new() { Bytes = -1 } }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    public async Task InvalidIds_AreRejectedBeforeSending(string? id)
    {
        using var client = NoRequests();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.GetAccessKeyAsync(id!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.DeleteAccessKeyAsync(id!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.CreateAccessKeyWithIdAsync(id!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.RenameAccessKeyAsync(id!, "name"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.SetAccessKeyDataLimitAsync(id!, 0));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.RemoveAccessKeyDataLimitAsync(id!));
    }

    [Theory]
    [InlineData(200, "{}")]
    [InlineData(200, "invalid")]
    [InlineData(500, "error")]
    public async Task ResponseContent_IsDisposedOnSuccessAndFailure(int status, string body)
    {
        var content = new TrackingContent(body);
        using var client = Create((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = content }));
        if (status == 500) await Assert.ThrowsAsync<OutLinkApiException>(() => client.GetServerAsync());
        else if (body == "invalid") await Assert.ThrowsAsync<JsonException>(() => client.GetServerAsync());
        else await client.GetServerAsync();
        Assert.True(content.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAndTimeout_ApplyWhileReadingBody(bool timeout)
    {
        using var stream = new BlockingStream();
        using var client = Create((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        }));
        client.HttpClient.Timeout = timeout ? TimeSpan.FromMilliseconds(100) : Timeout.InfiniteTimeSpan;
        using var cancellation = new CancellationTokenSource();
        var task = client.GetServerAsync(cancellation.Token);
        await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (!timeout) cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task ConcurrentRequests_KeepTheirOwnRequestAndResponseState()
    {
        var entered = 0;
        var allEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = Create(async (request, token) =>
        {
            if (Interlocked.Increment(ref entered) == 10) allEntered.SetResult();
            await allEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            var id = request.RequestUri!.Segments[^1];
            return Response("{\"id\":\"" + id + "\"}");
        });
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => client.GetAccessKeyAsync(i.ToString())));
        Assert.Equal(Enumerable.Range(0, 10).Select(i => i.ToString()), results.Select(key => key.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task MissingServerNameOrHostname_IsRejected(string? value)
    {
        using var client = NoRequests();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.RenameServerAsync(value!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.SetHostnameForAccessKeysAsync(value!));
    }

    private sealed class TrackingContent(string body) : StringContent(body)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class BlockingStream : Stream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private static Client NoRequests() => Create((_, _) => throw new Xunit.Sdk.XunitException("No request expected."));
    private static Client Create(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new HttpClient(new StubHandler(send)) { BaseAddress = new Uri("https://example.com/secret/") });
    private static HttpResponseMessage Response(string? body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = body is null ? null : new StringContent(body, Encoding.UTF8, "application/json") };
    private static void AssertJson(string expected, string actual) =>
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(expected), System.Text.Json.Nodes.JsonNode.Parse(actual)), actual);

#pragma warning disable CS0618 // The deprecated endpoints are intentionally covered.
    private static Task Invoke(Client client, string operation, CancellationToken token = default) => operation switch
    {
        "server" => client.GetServerAsync(token),
        "hostname" => client.SetHostnameForAccessKeysAsync("vpn.example.com", token),
        "port" => client.SetPortForNewAccessKeysAsync(65535, token),
        "default-limit" => client.SetAccessKeyDataLimitAsync(8589934592, token),
        "remove-default-limit" => client.RemoveAccessKeyDataLimitAsync(token),
        "experimental-metrics" => client.GetExperimentalServerMetricsAsync("24h", token),
        "rename-server" => client.RenameServerAsync("My server", token),
        "create" => client.CreateAccessKeyAsync(cancellationToken: token),
        "list" => client.GetAccessKeysAsync(token),
        "create-id" => client.CreateAccessKeyWithIdAsync("key", cancellationToken: token),
        "get" => client.GetAccessKeyAsync("key", token),
        "delete" => client.DeleteAccessKeyAsync("key", token),
        "rename-key" => client.RenameAccessKeyAsync("key", "", token),
        "key-limit" => client.SetAccessKeyDataLimitAsync("key", 0, token),
        "remove-key-limit" => client.RemoveAccessKeyDataLimitAsync("key", token),
        "transfer" => client.GetTransferMetricsAsync(token),
        "metrics-enabled" => client.GetMetricsEnabledAsync(token),
        "set-metrics" => client.SetMetricsEnabledAsync(false, token),
        "legacy-limit" => client.SetExperimentalAccessKeyDataLimitAsync(1, token),
        "legacy-remove" => client.RemoveExperimentalAccessKeyDataLimitAsync(token),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
#pragma warning restore CS0618

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
