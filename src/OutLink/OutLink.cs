using System.Security.Cryptography;

namespace OutLink;

/// <summary>Owns an isolated client for an Outline server's management API.</summary>
public sealed partial class OutLink : IDisposable
{
    private readonly HttpClientHandler? _handler;

    private OutLink(Uri apiUrl, byte[] certificateFingerprint)
    {
        _handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate is not null && CryptographicOperations.FixedTimeEquals(
                    certificate.GetCertHash(HashAlgorithmName.SHA256), certificateFingerprint)
        };

        HttpClient = new HttpClient(_handler)
        {
            BaseAddress = apiUrl
        };
    }

    // The test assembly supplies an in-memory transport without exposing a public bypass of TLS pinning.
    internal OutLink(HttpClient httpClient) => HttpClient = httpClient;

    /// <summary>
    /// Gets the owned client. Use relative API paths such as "access-keys".
    /// Its BaseAddress contains the secret API URL; do not log or share it.
    /// </summary>
    public HttpClient HttpClient { get; }

    /// <summary>Creates a client using an HTTPS API URL and Outline's certSha256 hex fingerprint.</summary>
    public static OutLink New(string apiUrl, string cert)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(cert);

        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new ArgumentException("An absolute HTTPS API URL without user information, query, or fragment is required.", nameof(apiUrl));
        }

        if (cert.Length != 64 || !cert.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("The certificate fingerprint must contain 64 hexadecimal SHA-256 characters.", nameof(cert));
        }

        // Preserve Outline's secret authentication path when resolving relative endpoints.
        var baseAddress = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
        return new OutLink(baseAddress, Convert.FromHexString(cert));
    }

    /// <summary>Disposes the client and its underlying handler.</summary>
    public void Dispose() => HttpClient.Dispose();
}
