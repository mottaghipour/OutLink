using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OutLink.Tests;

public class OutLineClientTests
{
    private static readonly string Fingerprint = new('a', 64);

    [Theory]
    [InlineData("https://example.com:1234/secret")]
    [InlineData("https://example.com:1234/secret/")]
    public void New_PreservesAuthenticationPathForRelativeEndpoints(string apiUrl)
    {
        using var client = OutLineClient.New(apiUrl, Fingerprint);

        Assert.Equal(new Uri("https://example.com:1234/secret/access-keys"),
            new Uri(client.HttpClient.BaseAddress!, "access-keys"));
    }

    [Fact]
    public async Task New_CreatesIndependentClientsAndLifetimes()
    {
        using var first = OutLineClient.New("https://example.com/first", Fingerprint);
        using var second = OutLineClient.New("https://example.com/second", Fingerprint);

        Assert.NotSame(first.HttpClient, second.HttpClient);
        first.HttpClient.DefaultRequestHeaders.Add("X-Test", "first");
        Assert.False(second.HttpClient.DefaultRequestHeaders.Contains("X-Test"));
        first.Dispose();
        first.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => first.HttpClient.GetAsync("server"));
        second.HttpClient.Timeout = TimeSpan.FromSeconds(15);
        Assert.Equal(TimeSpan.FromSeconds(15), second.HttpClient.Timeout);
        Assert.Equal(new Uri("https://example.com/second/"), second.HttpClient.BaseAddress);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative")]
    [InlineData("http://example.com/secret")]
    [InlineData("https://user:password@example.com/secret")]
    [InlineData("https://example.com/secret?q=1")]
    [InlineData("https://example.com/secret#fragment")]
    public void New_RejectsInvalidApiUrls(string? apiUrl)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => OutLineClient.New(apiUrl!, Fingerprint));
        Assert.Equal("apiUrl", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void New_RejectsMissingFingerprints(string? cert)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => OutLineClient.New("https://example.com/secret", cert!));
        Assert.Equal("cert", exception.ParamName);
    }

    [Theory]
    [InlineData('a', 63)]
    [InlineData('a', 65)]
    [InlineData('z', 64)]
    public void New_RejectsMalformedFingerprints(char character, int length)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            OutLineClient.New("https://example.com/secret", new string(character, length)));
        Assert.Equal("cert", exception.ParamName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CertificateValidation_AcceptsMatchingPinRegardlessOfHexCase(bool lowercase)
    {
        using var certificate = CreateCertificate();
        var fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        using var client = OutLineClient.New("https://example.com/secret",
            lowercase ? fingerprint.ToLowerInvariant() : fingerprint);
        using var message = new HttpRequestMessage();

        var accepted = Handler(client).ServerCertificateCustomValidationCallback!(
            message, certificate, null, SslPolicyErrors.RemoteCertificateChainErrors);

        Assert.True(accepted);
    }

    [Fact]
    public void CertificateValidation_RejectsMismatchingPinEvenWithoutPolicyErrors()
    {
        using var certificate = CreateCertificate();
        var fingerprint = certificate.GetCertHash(HashAlgorithmName.SHA256);
        fingerprint[0] ^= 0xff;
        using var client = OutLineClient.New("https://example.com/secret", Convert.ToHexString(fingerprint));
        using var message = new HttpRequestMessage();

        Assert.False(Handler(client).ServerCertificateCustomValidationCallback!(
            message, certificate, null, SslPolicyErrors.None));
    }

    [Fact]
    public void CertificateValidation_RejectsMissingCertificate()
    {
        using var client = OutLineClient.New("https://example.com/secret", Fingerprint);
        using var message = new HttpRequestMessage();

        Assert.False(Handler(client).ServerCertificateCustomValidationCallback!(
            message, null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
    }

    [Fact]
    public void New_DisablesRedirectsAndCookies()
    {
        using var client = OutLineClient.New("https://example.com/secret", Fingerprint);

        Assert.False(Handler(client).AllowAutoRedirect);
        Assert.False(Handler(client).UseCookies);
    }

    [Fact]
    public void PublicApi_ExposesOnlyReadOnlyClientAndNoPublicConstructorOrFields()
    {
        Assert.Empty(typeof(OutLineClient).GetConstructors());
        Assert.Empty(typeof(OutLineClient).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        var property = Assert.Single(typeof(OutLineClient).GetProperties());
        Assert.Equal("HttpClient", property.Name);
        Assert.Null(property.SetMethod);
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=example.com", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.Create(request.SubjectName,
            X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1),
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), new byte[] { 1 });
    }

    // Exercise TLS validation directly to keep these unit tests independent of network I/O.
    private static HttpClientHandler Handler(OutLineClient client) => (HttpClientHandler)typeof(OutLineClient)
        .GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;
}
