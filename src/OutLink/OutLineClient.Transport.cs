using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace OutLink;

public sealed partial class OutLineClient
{
    private static StringContent Json<T>(T value, JsonTypeInfo<T> type) =>
        new(JsonSerializer.Serialize(value, type), Encoding.UTF8, "application/json");

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, HttpContent? content,
        JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        using var timeout = CreateTimeout(cancellationToken);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, timeout.Token).ConfigureAwait(false);
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, type, timeout.Token).ConfigureAwait(false)
            ?? throw new JsonException("Outline API returned null instead of a JSON object.");
    }

    private async Task SendEmptyAsync(HttpMethod method, string path, HttpContent? content,
        CancellationToken cancellationToken, string? redirectPath = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        using var timeout = CreateTimeout(cancellationToken);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        // Only the two documented legacy routes may redirect, and only to our known modern route.
        // HttpStatusCode.PermanentRedirect is not defined by the .NET Standard 2.0 reference assembly.
        if ((int)response.StatusCode == 308 && redirectPath is not null)
        {
            using var redirected = new HttpRequestMessage(method, redirectPath) { Content = content };
            using var redirectedResponse = await HttpClient.SendAsync(redirected,
                HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            await EnsureSuccessAsync(redirectedResponse, timeout.Token).ConfigureAwait(false);
            return;
        }
        await EnsureSuccessAsync(response, timeout.Token).ConfigureAwait(false);
    }

    private CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(HttpClient.Timeout);
        return source;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        cancellationToken.ThrowIfCancellationRequested();
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        ApiError? error = null;
        try { error = JsonSerializer.Deserialize(body, OutLinkJsonContext.Default.ApiError); }
        catch (JsonException) { /* Non-JSON errors still preserve their HTTP status and body. */ }
        throw new OutLinkApiException(response.StatusCode, error?.Code, error?.Message, body);
    }
}
