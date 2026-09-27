using System.Net;
using System.Text;
using Account.Client;
using Account.Features.BackOffice.Queries;
using Account.Features.BackOffice.Requests;
using Blazor.Client.BackOffice;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using SharedKernel.Domain;
using SharedKernel.FeatureFlags;

namespace Blazor.Tests.Client.BackOffice;

// The back-office typed client reads the signed-in identity and sends the admin write the account API maps under
// /api/back-office, and its chain adds the host-issued antiforgery token to the write only
public sealed class BackOfficeClientTests
{
    [Fact]
    public async Task GetMeAsync_WhenTheAccountApiReturnsTheIdentity_ShouldReadTheMeResponse()
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.OK, """{"displayName":"Admin","email":"admin@dev.localhost","isAdmin":true,"groups":["BackOfficeAdmins"]}""");
        var client = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await client.GetMeAsync(CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new MeResponse("Admin", "admin@dev.localhost", true, ["BackOfficeAdmins"]));
        network.Requests.Single().Method.Should().Be(HttpMethod.Get);
        network.Requests.Single().Uri.Should().Be(new Uri("https://back-office.dev.localhost:9001/api/back-office/me"));
    }

    [Fact]
    public async Task GetMeAsync_WhenTheAccountApiAnswersUnauthorized_ShouldReportUnauthorized()
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.Unauthorized, "");
        var client = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await client.GetMeAsync(CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.Unauthorized);
    }

    [Theory]
    [InlineData(AbInclusionPin.AlwaysOn, """{"abInclusionPin":"AlwaysOn"}""")]
    [InlineData(null, """{"abInclusionPin":null}""")]
    public async Task SetTenantAbInclusionPinAsync_ShouldPutThePinToTheTenantRouteWithTheHostIssuedToken(AbInclusionPin? pin, string expectedBody)
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.OK, "");
        var chain = new AntiforgeryHeaderHandler(new BackOfficeAntiforgeryTokenSource(new FixedAntiforgeryStateProvider("host-issued-token"))) { InnerHandler = network };
        var client = new BackOfficeClient(new HttpClient(chain) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await client.SetTenantAbInclusionPinAsync(new TenantId(4711), new SetTenantAbInclusionPinCommand(pin), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var request = network.Requests.Single();
        request.Method.Should().Be(HttpMethod.Put);
        request.Uri.Should().Be(new Uri("https://back-office.dev.localhost:9001/api/back-office/tenants/4711/ab-inclusion-pin"));
        request.Body.Should().Be(expectedBody);
        request.AntiforgeryToken.Should().Be("host-issued-token");
    }

    [Fact]
    public async Task GetMeAsync_WhenSentThroughTheAntiforgeryChain_ShouldCarryNoToken()
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.OK, """{"displayName":"User","email":"user@dev.localhost","isAdmin":false,"groups":[]}""");
        var chain = new AntiforgeryHeaderHandler(new BackOfficeAntiforgeryTokenSource(new FixedAntiforgeryStateProvider("host-issued-token"))) { InnerHandler = network };
        var client = new BackOfficeClient(new HttpClient(chain) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        await client.GetMeAsync(CancellationToken.None);

        // Assert
        network.Requests.Single().AntiforgeryToken.Should().BeNull();
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, string? AntiforgeryToken);

    private sealed class FixedAntiforgeryStateProvider(string token) : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken()
        {
            return new AntiforgeryRequestToken(token, "__RequestVerificationToken");
        }
    }

    private sealed class RecordingNetwork(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var antiforgeryToken = request.Headers.TryGetValues(AccountApiHeaders.AntiforgeryToken, out var values) ? values.Single() : null;
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, requestBody, antiforgeryToken));
            var mediaType = statusCode == HttpStatusCode.OK ? "application/json" : "application/problem+json";
            return new HttpResponseMessage(statusCode) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
        }
    }
}
