using System.Net;

namespace OutLink;

/// <summary>An unsuccessful HTTP response from the Outline API.</summary>
public sealed class OutLinkApiException : HttpRequestException
{
    internal OutLinkApiException(HttpStatusCode statusCode, string? code, string? apiMessage, string responseBody)
        : base($"Outline API returned HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
        Code = code;
        ApiMessage = apiMessage;
        ResponseBody = responseBody;
    }

    public string? Code { get; }
    public string? ApiMessage { get; }
    public HttpStatusCode StatusCode { get; }
    /// <summary>The response body, which may contain sensitive server-provided information.</summary>
    public string ResponseBody { get; }
}
