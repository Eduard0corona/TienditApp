using System.Net;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>Answers every request with a fixed provider response and records what was sent.</summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler, IHttpClientFactory
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;

    public StubHttpMessageHandler(HttpStatusCode statusCode, string responseBody = "")
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    public List<RecordedRequest> Requests { get; } = new();

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var content = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(request.RequestUri!, content));
        return new HttpResponseMessage(_statusCode) { Content = new StringContent(_responseBody) };
    }
}

public sealed record RecordedRequest(Uri Uri, string Content);
