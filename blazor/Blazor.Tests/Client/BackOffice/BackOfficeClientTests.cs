using System.Net;
using System.Text;
using Account.Client;
using Account.Features.BackOffice.Queries;
using Account.Features.BackOffice.Requests;
using Account.Features.Tenants.BackOffice.Commands;
using Account.Features.Users.BackOffice.Requests;
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
    public async Task ReconcileTenantWithStripeAsync_ShouldPostToTheTenantRouteWithTheTokenAndReadTheResult()
    {
        // Arrange
        var network = new RecordingNetwork(
            HttpStatusCode.OK,
            """{"billingEventsAppended":2,"hasDriftDetected":false,"driftDiscrepancyCount":0,"reconciledAt":"2026-09-27T12:00:00+00:00","archivedEventsAwaitingConfirmation":{"count":3,"oldestOccurredAt":"2026-05-01T00:00:00+00:00","newestOccurredAt":"2026-06-01T00:00:00+00:00"}}"""
        );
        var chain = new AntiforgeryHeaderHandler(new BackOfficeAntiforgeryTokenSource(new FixedAntiforgeryStateProvider("host-issued-token"))) { InnerHandler = network };
        var client = new BackOfficeClient(new HttpClient(chain) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await client.ReconcileTenantWithStripeAsync(new TenantId(4711), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.BillingEventsAppended.Should().Be(2);
        result.Value.ArchivedEventsAwaitingConfirmation.Should().Be(
            new ArchivedEventsAwaitingConfirmation(3, new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero))
        );
        var request = network.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri("https://back-office.dev.localhost:9001/api/back-office/tenants/4711/reconcile-with-stripe"));
        request.AntiforgeryToken.Should().Be("host-issued-token");
    }

    [Fact]
    public async Task ReplayArchivedTenantStripeEventsAsync_ShouldPostToTheTenantRouteWithTheTokenAndReadTheResult()
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.OK, """{"billingEventsAppended":4,"replayedAt":"2026-09-27T12:00:00+00:00"}""");
        var chain = new AntiforgeryHeaderHandler(new BackOfficeAntiforgeryTokenSource(new FixedAntiforgeryStateProvider("host-issued-token"))) { InnerHandler = network };
        var client = new BackOfficeClient(new HttpClient(chain) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await client.ReplayArchivedTenantStripeEventsAsync(new TenantId(4711), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ReplayArchivedTenantStripeEventsResponse(4, new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero)));
        var request = network.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri("https://back-office.dev.localhost:9001/api/back-office/tenants/4711/replay-archived-stripe-events"));
        request.AntiforgeryToken.Should().Be("host-issued-token");
    }

    [Fact]
    public async Task ReconcileTenantWithStripeAsync_WhenTheAccountApiRefusesANonAdmin_ShouldReportTheForbiddenStatus()
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.Forbidden, "");
        var client = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await client.ReconcileTenantWithStripeAsync(new TenantId(4711), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Problem!.StatusCode.Should().Be(403);
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

    [Theory]
    [InlineData(AbInclusionPin.NeverOn, """{"abInclusionPin":"NeverOn"}""")]
    [InlineData(null, """{"abInclusionPin":null}""")]
    public async Task SetUserAbInclusionPinAsync_ShouldPutThePinToTheUserRouteWithTheHostIssuedToken(AbInclusionPin? pin, string expectedBody)
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.OK, "");
        var chain = new AntiforgeryHeaderHandler(new BackOfficeAntiforgeryTokenSource(new FixedAntiforgeryStateProvider("host-issued-token"))) { InnerHandler = network };
        var client = new BackOfficeClient(new HttpClient(chain) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await client.SetUserAbInclusionPinAsync(new UserId("usr_01JMVAW4T4320KJ3A7EJMCG8R0"), new SetUserAbInclusionPinCommand(pin), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var request = network.Requests.Single();
        request.Method.Should().Be(HttpMethod.Put);
        request.Uri.Should().Be(new Uri("https://back-office.dev.localhost:9001/api/back-office/users/usr_01JMVAW4T4320KJ3A7EJMCG8R0/ab-inclusion-pin"));
        request.Body.Should().Be(expectedBody);
        request.AntiforgeryToken.Should().Be("host-issued-token");
    }

    [Fact]
    public async Task SetUserAbInclusionPinAsync_WhenTheAccountApiRefusesANonAdmin_ShouldReportTheForbiddenStatus()
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.Forbidden, """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.4","title":"Forbidden","status":403}""");
        var client = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await client.SetUserAbInclusionPinAsync(new UserId("usr_01JMVAW4T4320KJ3A7EJMCG8R0"), new SetUserAbInclusionPinCommand(AbInclusionPin.AlwaysOn), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Problem!.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetUserAsync_ShouldReadTheUserWithItsMembershipsAndPin()
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.OK, """
                                                              {"id":"usr_01JMVAW4T4320KJ3A7EJMCG8R0","tenantId":"42","tenantName":"Acme","email":"ann@example.com","firstName":"Ann",
                                                               "lastName":null,"title":null,"role":"Owner","emailConfirmed":true,"locale":"en-US","createdAt":"2026-01-01T00:00:00+00:00",
                                                               "modifiedAt":null,"lastSeenAt":null,"avatarUrl":null,"abInclusionPin":"AlwaysOn","tenantMemberships":[{"userId":"usr_01JMVAW4T4320KJ3A7EJMCG8R0",
                                                               "tenantId":"42","tenantName":"Acme","tenantLogoUrl":null,"plan":"Standard","plannedChange":"Cancellation","hasEverSubscribed":true,
                                                               "monthlyRecurringRevenue":29,"scheduledPriceAmount":null,"currency":"EUR","renewalDate":null,"country":"DK","role":"Owner",
                                                               "emailConfirmed":true,"createdAt":"2026-01-01T00:00:00+00:00","lastSeenAt":null}]}
                                                              """
        );
        var client = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await client.GetUserAsync(new UserId("usr_01JMVAW4T4320KJ3A7EJMCG8R0"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.AbInclusionPin.Should().Be(AbInclusionPin.AlwaysOn);
        result.Value.TenantMemberships.Should().ContainSingle().Which.TenantId.Should().Be(new TenantId(42));
        network.Requests.Single().Uri.Should().Be(new Uri("https://back-office.dev.localhost:9001/api/back-office/users/usr_01JMVAW4T4320KJ3A7EJMCG8R0"));
    }

    [Theory]
    [InlineData(0, 1, "/api/back-office/users/usr_01JMVAW4T4320KJ3A7EJMCG8R0/sessions?PageSize=1")]
    [InlineData(2, 25, "/api/back-office/users/usr_01JMVAW4T4320KJ3A7EJMCG8R0/sessions?PageOffset=2&PageSize=25")]
    public async Task GetUserSessionsAsync_ShouldSendThePageToTheSessionsRoute(int pageOffset, int pageSize, string expectedPathAndQuery)
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.OK, """{"totalCount":0,"pageSize":1,"totalPages":0,"currentPageOffset":0,"sessions":[]}""");
        var client = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await client.GetUserSessionsAsync(new UserId("usr_01JMVAW4T4320KJ3A7EJMCG8R0"), new GetBackOfficeUserSessionsQuery(pageOffset, pageSize), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        network.Requests.Single().Uri.PathAndQuery.Should().Be(expectedPathAndQuery);
    }

    [Fact]
    public async Task GetUserLoginHistoryAsync_ShouldReadTheEntriesFromTheLoginHistoryRoute()
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.OK, """
                                                              {"entries":[{"kind":"Email","method":"OneTimePassword","outcome":"Failed","occurredAt":"2026-09-01T10:00:00+00:00",
                                                               "failureReason":"TooManyRetries","externalProvider":null}]}
                                                              """
        );
        var client = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await client.GetUserLoginHistoryAsync(new UserId("usr_01JMVAW4T4320KJ3A7EJMCG8R0"), CancellationToken.None);

        // Assert
        result.Value!.Entries.Should().ContainSingle().Which.FailureReason.Should().Be("TooManyRetries");
        network.Requests.Single().Uri.AbsolutePath.Should().Be("/api/back-office/users/usr_01JMVAW4T4320KJ3A7EJMCG8R0/login-history");
    }

    [Fact]
    public async Task GetUserFeatureFlagsAsync_ShouldReadTheFlagsFromTheFeatureFlagsRoute()
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.OK, """
                                                              {"flags":[{"flagKey":"beta-features","scope":"User","description":"Beta","isAbTestEligible":true,"bucketStart":null,
                                                               "bucketEnd":null,"rolloutPercentage":10,"isEnabled":true,"source":"manual_override","isBaseRowActive":true,"rolloutBucket":3,
                                                               "tenantId":"42","inclusionThresholdPercentage":null,"defaultEnabled":false,"userAbInclusionPin":null}]}
                                                              """
        );
        var client = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await client.GetUserFeatureFlagsAsync(new UserId("usr_01JMVAW4T4320KJ3A7EJMCG8R0"), CancellationToken.None);

        // Assert
        result.Value!.Flags.Should().ContainSingle().Which.IsEnabled.Should().BeTrue();
        network.Requests.Single().Uri.AbsolutePath.Should().Be("/api/back-office/users/usr_01JMVAW4T4320KJ3A7EJMCG8R0/feature-flags");
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
