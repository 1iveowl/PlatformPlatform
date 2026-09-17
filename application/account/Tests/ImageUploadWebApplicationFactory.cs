using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using SharedKernel.Authentication;
using SharedKernel.Integrations.BlobStorage;
using SharedKernel.Tests;

namespace Account.Tests;

// Upload tests run with antiforgery validation active, so they share the antiforgery collection that runs after the
// parallel collections, and they replace blob storage with a substitute to prove whether a blob was written.
public sealed class ImageUploadWebApplicationFactory : AccountWebApplicationFactory
{
    public ImageUploadWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("BypassAntiforgeryValidation", "false");
    }

    // Shared across every test in a class, so each test calls ClearReceivedCalls() before acting
    public IBlobStorageClient BlobStorageClient { get; } = Substitute.For<IBlobStorageClient>();

    public static async Task<(string Cookie, string RequestToken)> GetAntiforgeryPairAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/account/bootstrap");
        response.ShouldBeSuccessfulGetRequest();
        var cookie = response.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith($"{AuthenticationTokenHttpKeys.AntiforgeryTokenCookieName}=", StringComparison.Ordinal))
            .Split(';')[0];
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return (cookie, body.GetProperty("antiforgeryToken").GetString()!);
    }

    public static HttpRequestMessage CreateUploadRequest(string url, HttpContent content, string? antiforgeryCookie, string? requestToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (antiforgeryCookie is not null)
        {
            request.Headers.Add("Cookie", antiforgeryCookie);
        }

        if (requestToken is not null)
        {
            request.Headers.Add(AuthenticationTokenHttpKeys.AntiforgeryTokenHttpHeaderKey, requestToken);
        }

        return request;
    }

    public static async Task AssertAntiforgeryRejectedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("title").GetString().Should().Be("Invalid Antiforgery Token");
    }

    protected override void ConfigureAdditionalTestServices(IServiceCollection services)
    {
        services.RemoveAll(typeof(IBlobStorageClient));
        services.AddKeyedSingleton("account-storage", BlobStorageClient);
    }

    protected override void Dispose(bool disposing)
    {
        Environment.SetEnvironmentVariable("BypassAntiforgeryValidation", "true");
        base.Dispose(disposing);
    }
}
