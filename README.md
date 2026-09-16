# OutLink

**A dependency-free .NET library for managing Outline VPN servers.**

OutLink wraps the Outline Server Management REST API with typed asynchronous methods. Manage server settings, create and revoke access keys, configure data limits, and read usage metrics through one isolated client instance.

The library targets **.NET Standard 2.0**, making it compatible with modern .NET applications and other .NET implementations that support .NET Standard 2.0. It implements all **20 operations** from the [Outline OpenAPI document](https://github.com/OutlineFoundation/outline-server/blob/master/src/shadowbox/server/api.yml) reviewed on September 13, 2026, including experimental and deprecated operations.

## Contents

- [Features](#features)
- [Getting started](#getting-started)
- [Connection and client configuration](#connection-and-client-configuration)
- [Server management](#server-management)
- [Access keys](#access-keys)
- [Data limits](#data-limits)
- [Metrics](#metrics)
- [Cancellation and error handling](#cancellation-and-error-handling)
- [Complete API reference](#complete-api-reference)
- [Response models](#response-models)
- [Build and test](#build-and-test)
- [Project structure](#project-structure)

## Features

- An independent `HttpClient` and certificate pin for each `OutLineClient` instance.
- HTTPS server verification using Outline's SHA-256 certificate fingerprint.
- Typed request options and response models.
- Asynchronous operations with cancellation support.
- Connection pooling through a reusable HTTP client.
- Streamed JSON responses and source-generated `System.Text.Json` serialization metadata.
- API errors that preserve HTTP status and server error details.
- Unit tests using the existing xUnit test packages; no live server required.

OutLink manages a server's API. It does not establish a VPN connection or install an Outline server.

## Getting started

### Requirements

- A .NET implementation compatible with .NET Standard 2.0.
- An Outline server's management `apiUrl` and `certSha256` values.
- Network access from your application to that management endpoint.

### Reference the library

Install OutLink from NuGet:

```sh
dotnet add package OutLink --version 1.0.0
```

To use a local checkout, add a project reference to your application's `.csproj`. Adjust the relative path for your directory structure:

```xml
<ItemGroup>
  <ProjectReference Include="../OutLink/src/OutLink/OutLink.csproj" />
</ItemGroup>
```

To generate a local NuGet package from the repository root:

```sh
dotnet pack src/OutLink/OutLink.csproj --configuration Release --output artifacts
```

This creates a package locally; it does not publish it to a feed.

### First request

Import the `OutLink` namespace and create an `OutLineClient`:

```csharp
using OutLink;

var apiUrl = Environment.GetEnvironmentVariable("OUTLINE_API_URL")
    ?? throw new InvalidOperationException("OUTLINE_API_URL is missing.");
var certSha256 = Environment.GetEnvironmentVariable("OUTLINE_CERT_SHA256")
    ?? throw new InvalidOperationException("OUTLINE_CERT_SHA256 is missing.");

using var outline = OutLineClient.New(apiUrl, certSha256);

var server = await outline.GetServerAsync();
Console.WriteLine($"Server: {server.Name}, version: {server.Version}");
```

These environment variable names are an example application convention. OutLink receives the values as arguments and does not read configuration automatically.

The following examples assume an initialized `outline` instance. Add `using OutLink;` when using model names such as `CreateAccessKeyOptions` and `DataLimit`.

## Connection and client configuration

```csharp
using OutLink;

using var outline = OutLineClient.New(apiUrl, certSha256);
outline.HttpClient.Timeout = TimeSpan.FromSeconds(30);
```

| Argument | Format and behavior |
| --- | --- |
| `apiUrl` | An absolute HTTPS management URL, including its secret path, such as `https://vpn.example.com:12345/secret-path`. User information, query strings, and fragments are rejected. A trailing slash is added automatically. |
| `cert` | Outline's `certSha256`: exactly 64 hexadecimal characters, case-insensitive. Pass the fingerprint without separators, not a PEM certificate or certificate file path. |

Construction validates the arguments and creates the HTTP client; it does not contact the server. TLS verification happens when a request is sent. The configured pin must match the server certificate's SHA-256 fingerprint, including when that certificate is self-signed. Authentication uses the secret path already present in the management URL; no bearer token is required by this client.

Reuse an instance across requests so its HTTP connections can be pooled. Concurrent API calls are supported. Configure the exposed client before sending requests, and dispose the `OutLineClient` instance only after its outstanding operations finish. Disposing it also disposes its HTTP client and handler. Create separate instances to manage separate servers.

There are no public URL or certificate properties. However, the exposed `HttpClient.BaseAddress` contains the API URL, including its secret path. Treat it as a credential. Access-key `Password` and `AccessUrl` values also contain credentials.

### Direct HTTP access

The owned client is available for custom requests:

```csharp
using var response = await outline.HttpClient.GetAsync("server");
response.EnsureSuccessStatusCode();
var json = await response.Content.ReadAsStringAsync();
```

Use relative paths **without a leading slash** to retain the secret API path. Dispose responses you obtain directly. Direct requests use normal `HttpClient` behavior; OutLink's typed deserialization and `OutLinkApiException` handling apply to its API methods.

## Server management

```csharp
var server = await outline.GetServerAsync();

await outline.RenameServerAsync("Production VPN");
await outline.SetHostnameForAccessKeysAsync("vpn.example.com");
await outline.SetPortForNewAccessKeysAsync(12345);
```

The new-access-key port must be between `1` and `65535`. Hostname and server-name arguments must be nonempty. Server-side validation can impose additional requirements and returns an API error when a setting is rejected. Changing a hostname through this API does not configure DNS.

## Access keys

### Create with server defaults

```csharp
var key = await outline.CreateAccessKeyAsync();
```

### Create with options

```csharp
var key = await outline.CreateAccessKeyAsync(new CreateAccessKeyOptions
{
    Name = "Personal phone",
    Method = "aes-192-gcm",
    Password = "replace-with-your-generated-password",
    Port = 12345,
    Limit = new DataLimit { Bytes = 10L * 1024 * 1024 * 1024 }
});
```

All creation options are optional. Unset (`null`) properties are omitted from JSON so the server can choose its defaults. You can supply only the fields you need:

```csharp
var key = await outline.CreateAccessKeyAsync(new CreateAccessKeyOptions
{
    Name = "Laptop"
});
```

| Option | Type | Meaning |
| --- | --- | --- |
| `Name` | `string?` | Access-key label. |
| `Method` | `string?` | Encryption method accepted by the server. |
| `Password` | `string?` | Access-key password; omit to let the server generate it. |
| `Port` | `int?` | Port from `1` to `65535`. |
| `Limit` | `DataLimit?` | Key-specific allowance with a non-negative `long Bytes` value. |

### Create with a specific identifier

```csharp
var key = await outline.CreateAccessKeyWithIdAsync(
    "personal-laptop",
    new CreateAccessKeyOptions { Name = "Laptop" });
```

Identifiers are strings, not necessarily numbers. OutLink escapes them as URL path segments. Null, empty, whitespace-only, `.` and `..` identifiers are rejected.

### List, retrieve, rename, and delete

```csharp
var keys = await outline.GetAccessKeysAsync();
var key = await outline.GetAccessKeyAsync("personal-laptop");

await outline.RenameAccessKeyAsync(key.Id, "Work laptop");
await outline.RenameAccessKeyAsync(key.Id, ""); // Clear the label.
await outline.DeleteAccessKeyAsync(key.Id);
```

`GetAccessKeysAsync` returns `IReadOnlyList<AccessKey>`. Retrieving or deleting an unknown key can raise `OutLinkApiException` with HTTP status `404`.

## Data limits

Limits are expressed in bytes using `long`. For example, `10L * 1024 * 1024 * 1024` is 10 GiB. Negative values are rejected. Zero is a valid allowance; use a remove method to remove a limit.

### Server default

```csharp
await outline.SetAccessKeyDataLimitAsync(10L * 1024 * 1024 * 1024);
await outline.RemoveAccessKeyDataLimitAsync();
```

### Individual key override

```csharp
await outline.SetAccessKeyDataLimitAsync("personal-laptop", 20L * 1024 * 1024 * 1024);
await outline.RemoveAccessKeyDataLimitAsync("personal-laptop");
```

Removing a key-specific override restores the server default if one exists. Removing the server default does not remove explicit per-key overrides.

Limit setters send `{"limit":{"bytes":value}}`, matching the OpenAPI examples and server implementation. Key creation uses the `Limit` option; returned access keys expose `DataLimit`, and server configuration exposes `AccessKeyDataLimit`.

## Metrics

### Data transferred per access key

```csharp
var transferred = await outline.GetTransferMetricsAsync();

foreach (var entry in transferred)
{
    Console.WriteLine($"Key {entry.Key}: {entry.Value} bytes");
}
```

The result is an `IReadOnlyDictionary<string, long>`, preserving custom identifiers and counters larger than 32-bit integers.

### Metrics sharing

```csharp
bool enabled = await outline.GetMetricsEnabledAsync();
await outline.SetMetricsEnabledAsync(true);
await outline.SetMetricsEnabledAsync(false);
```

This setting controls metrics sharing on the server.

### Experimental server metrics

```csharp
var metrics = await outline.GetExperimentalServerMetricsAsync("24h");
var defaultRange = await outline.GetExperimentalServerMetricsAsync();

var seconds = metrics.Server?.TunnelTime?.Seconds;
var bytes = metrics.Server?.DataTransferred?.Bytes;
var currentBandwidth = metrics.Server?.Bandwidth?.Current;

foreach (var keyMetrics in metrics.AccessKeys)
{
    var peakDevices = keyMetrics.Connection?.PeakDeviceCount?.Data;
}
```

`since` is an optional time-range string. OutLink URL-encodes it and leaves range validation to the server. This endpoint is experimental and may change or disappear. Nested metrics and location metadata can be absent or null; check them before use.

### Deprecated data-limit methods

`SetExperimentalAccessKeyDataLimitAsync(bytes)` and `RemoveExperimentalAccessKeyDataLimitAsync()` remain available for the two deprecated operations. Both carry `[Obsolete]`; use the stable server-default methods instead.

For these two legacy calls, a `308` response causes OutLink to repeat the operation at the known stable route on the configured server. It does not follow arbitrary redirect destinations. Other API methods do not follow redirects automatically.

## Cancellation and error handling

Every API method accepts an optional `CancellationToken`. Use a named argument when skipping optional arguments:

```csharp
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

var key = await outline.CreateAccessKeyAsync(
    cancellationToken: cancellation.Token);
```

The configured `HttpClient.Timeout` also applies while OutLink reads response bodies. Set it before the first request. Caller cancellation or a timeout can throw `OperationCanceledException`, including its subclass `TaskCanceledException`.

```csharp
using System.Net;
using System.Text.Json;
using OutLink;

try
{
    var key = await outline.GetAccessKeyAsync("personal-laptop");
}
catch (OutLinkApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
{
    Console.WriteLine("The access key does not exist.");
}
catch (OutLinkApiException exception)
{
    Console.WriteLine($"Outline API returned HTTP {(int?)exception.StatusCode}.");
    // Inspect Code, ApiMessage, and ResponseBody if needed.
}
catch (OperationCanceledException)
{
    Console.WriteLine("The request was canceled or timed out.");
}
catch (HttpRequestException)
{
    Console.WriteLine("The HTTP request failed.");
}
catch (JsonException)
{
    Console.WriteLine("The server returned an invalid JSON response.");
}
```

| Exception | Meaning |
| --- | --- |
| `ArgumentException` and subclasses | Invalid local input, such as a malformed fingerprint, missing identifier, invalid port, or negative limit. |
| `OutLinkApiException` | An HTTP response outside the successful 2xx range. Inherits from `HttpRequestException`. |
| `HttpRequestException` | Transport failure, such as a connection or TLS verification failure. |
| `OperationCanceledException` | Cancellation or timeout. |
| `JsonException` | A success response cannot be deserialized, is JSON `null`, or is missing a required JSON member. |
| `ObjectDisposedException` | An operation uses an already-disposed client. |

`OutLinkApiException` exposes `StatusCode`, optional server `Code` and `ApiMessage`, and the raw `ResponseBody`. Non-JSON error bodies are preserved. Its default message contains only the HTTP status; server-provided details can contain sensitive information.

OutLink does not automatically retry failed operations. If a mutation times out, check the server state before deciding whether to repeat it.

## Complete API reference

Each signature below omits its final optional `CancellationToken cancellationToken = default` parameter. `Task` means the operation has no response value.

| HTTP operation | OutLink method | Return type |
| --- | --- | --- |
| `GET /server` | `GetServerAsync()` | `Task<Server>` |
| `PUT /server/hostname-for-access-keys` | `SetHostnameForAccessKeysAsync(string hostname)` | `Task` |
| `PUT /server/port-for-new-access-keys` | `SetPortForNewAccessKeysAsync(int port)` | `Task` |
| `PUT /server/access-key-data-limit` | `SetAccessKeyDataLimitAsync(long bytes)` | `Task` |
| `DELETE /server/access-key-data-limit` | `RemoveAccessKeyDataLimitAsync()` | `Task` |
| `PUT /name` | `RenameServerAsync(string name)` | `Task` |
| `POST /access-keys` | `CreateAccessKeyAsync(CreateAccessKeyOptions? options = null)` | `Task<AccessKey>` |
| `GET /access-keys` | `GetAccessKeysAsync()` | `Task<IReadOnlyList<AccessKey>>` |
| `PUT /access-keys/{id}` | `CreateAccessKeyWithIdAsync(string id, CreateAccessKeyOptions? options = null)` | `Task<AccessKey>` |
| `GET /access-keys/{id}` | `GetAccessKeyAsync(string id)` | `Task<AccessKey>` |
| `DELETE /access-keys/{id}` | `DeleteAccessKeyAsync(string id)` | `Task` |
| `PUT /access-keys/{id}/name` | `RenameAccessKeyAsync(string id, string name)` | `Task` |
| `PUT /access-keys/{id}/data-limit` | `SetAccessKeyDataLimitAsync(string id, long bytes)` | `Task` |
| `DELETE /access-keys/{id}/data-limit` | `RemoveAccessKeyDataLimitAsync(string id)` | `Task` |
| `GET /metrics/transfer` | `GetTransferMetricsAsync()` | `Task<IReadOnlyDictionary<string, long>>` |
| `GET /metrics/enabled` | `GetMetricsEnabledAsync()` | `Task<bool>` |
| `PUT /metrics/enabled` | `SetMetricsEnabledAsync(bool enabled)` | `Task` |
| `GET /experimental/server/metrics` | `GetExperimentalServerMetricsAsync(string? since = null)` | `Task<ExperimentalMetrics>` |
| `PUT /experimental/access-key-data-limit` | `SetExperimentalAccessKeyDataLimitAsync(long bytes)` — obsolete | `Task` |
| `DELETE /experimental/access-key-data-limit` | `RemoveExperimentalAccessKeyDataLimitAsync()` — obsolete | `Task` |

## Response models

All public models are in the `OutLink` namespace and expose properties with `init` accessors.

| Model | Properties |
| --- | --- |
| `Server` | `Name`, `ServerId`, `MetricsEnabled`, `CreatedTimestampMs`, `Version`, `PortForNewAccessKeys`, `HostnameForAccessKeys`, `AccessKeyDataLimit` |
| `AccessKey` | Required `Id`; `Name`, `Password`, `Port`, `Method`, `AccessUrl`, `DataLimit` |
| `DataLimit` | `Bytes` (`long`) |
| `ExperimentalMetrics` | `Server`, `AccessKeys` |
| `ServerMetrics` | `TunnelTime`, `DataTransferred`, `Bandwidth`, `Locations` |
| `TunnelTime` | `Seconds` (`double`) |
| `TransferredData` | `Bytes` (`double`, matching the experimental API's numeric field) |
| `BandwidthMetrics` | `Current`, `Peak` |
| `BandwidthSample` | `Data` (`TransferredData`), `Timestamp` (`long`) |
| `LocationMetrics` | `Location`, nullable `Asn`, nullable `AsOrg`, `TunnelTime`, `DataTransferred` |
| `AccessKeyMetrics` | `AccessKeyId` (`long`, as defined by the experimental schema), `TunnelTime`, `DataTransferred`, `Connection` |
| `ConnectionMetrics` | Nullable `LastTrafficSeen`, `PeakDeviceCount` |
| `DeviceCountSample` | `Data` (`long`), `Timestamp` (`long`) |

`CreatedTimestampMs` is a Unix timestamp in milliseconds. Experimental timestamp values are returned as supplied by the API, without conversion to `DateTime`. Optional fields are nullable where modeled; omitted non-nullable numeric and Boolean fields retain their .NET defaults. Unknown JSON fields are ignored for forward compatibility.

See [Models.cs](src/OutLink/Models.cs) for exact property types and nullability.

## Build and test

Run these commands from the repository root:

```sh
dotnet restore OutLink.slnx
dotnet build OutLink.slnx --configuration Release --no-restore
dotnet test OutLink.slnx --configuration Release --no-build
```

To collect coverage using the existing collector:

```sh
dotnet test OutLink.slnx --collect:"XPlat Code Coverage"
```

Unit tests use in-memory HTTP handlers and do not require a running Outline server. They cover endpoint routing, request payloads, response models, TLS pin validation, local input validation, API and transport errors, legacy redirects, cancellation, body-read timeouts, disposal, and concurrent requests. They do not verify connectivity to a deployed server.

The library has no third-party package dependencies. The test project uses its existing defaults: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, and `coverlet.collector`.

## Project structure

```text
OutLink.slnx
src/
  OutLink/
    OutLineClient.cs                Instance creation, certificate pinning, and lifetime
    OutLineClient.Api.cs            Public API methods and request validation
    OutLineClient.Transport.cs      Shared HTTP and response handling
    OutLinkJsonContext.cs     Source-generated JSON metadata and internal payloads
    Models.cs                 Public request and response models
    OutLinkApiException.cs    API error details
    AssemblyInfo.cs           Internal access for the unit-test assembly
tests/
  OutLink.Tests/
    OutLineClientTests.cs           Client configuration and certificate tests
    ApiTests.cs               API and transport unit tests
```

Keep library code in `src` and unit tests in `tests`. New library functionality should use built-in .NET APIs and extend the existing unit-test suite without adding package dependencies.
