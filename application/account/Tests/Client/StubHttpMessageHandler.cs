using System.Net;
using System.Text;

namespace Account.Tests.Client;

// Records every request the client sends, including its body as sent, and answers without a network
public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    public static StubHttpMessageHandler Returning(HttpStatusCode statusCode, string? body = null, string contentType = "application/json")
    {
        return new StubHttpMessageHandler((_, _) => Task.FromResult(CreateResponse(statusCode, body, contentType)));
    }

    public static StubHttpMessageHandler Throwing(Exception exception)
    {
        return new StubHttpMessageHandler((_, _) => Task.FromException<HttpResponseMessage>(exception));
    }

    public static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string? body = null, string contentType = "application/json")
    {
        var response = new HttpResponseMessage(statusCode);
        if (body is not null) response.Content = new StringContent(body, Encoding.UTF8, contentType);
        return response;
    }

    public static HttpClient CreateHttpClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler) { BaseAddress = new Uri("https://localhost:9000") };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.PathAndQuery, body, headers));
        return await respond(request, cancellationToken);
    }
}

public sealed record RecordedRequest(HttpMethod Method, string PathAndQuery, string? Body, Dictionary<string, string[]> Headers);
