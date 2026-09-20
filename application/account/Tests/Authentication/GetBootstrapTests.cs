using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Account.Database;
using Account.Features.Authentication.Queries;
using FluentAssertions;
using SharedKernel.Tests;
using Xunit;

namespace Account.Tests.Authentication;

public sealed class GetBootstrapTests(AccountWebApplicationFactory factory) : EndpointBaseTest<AccountDbContext>(factory), IClassFixture<AccountWebApplicationFactory>
{
    private const string BootstrapUrl = "/api/account/bootstrap";

    private static readonly string[] SystemFeatureFlagKeys = ["google-oauth", "entra-oauth", "mitid-verification", "mitid-login", "subscriptions", "push-notifications-enabled"];

    private static readonly string[] RuntimeConfigurationKeys =
    [
        "PUBLIC_URL", "CDN_URL", "APPLICATION_VERSION", "PUBLIC_PUSH_PUBLIC_KEY", "PUBLIC_GOOGLE_OAUTH_ENABLED",
        "PUBLIC_ENTRA_OAUTH_ENABLED", "PUBLIC_MITID_VERIFICATION_ENABLED", "PUBLIC_MITID_LOGIN_ENABLED",
        "PUBLIC_SUBSCRIPTION_ENABLED", "PUBLIC_PUSH_NOTIFICATIONS_ENABLED"
    ];

    [Fact]
    public async Task GetBootstrap_WhenAnonymous_ShouldReturnRuntimeConfigurationAndNoIdentity()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, BootstrapUrl);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("da-DK"));

        // Act
        var response = await AnonymousHttpClient.SendAsync(request);

        // Assert
        response.ShouldBeSuccessfulGetRequest();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var bootstrap = await ReadJsonAsync(response);
        bootstrap.GetProperty("isAuthenticated").GetBoolean().Should().BeFalse();
        bootstrap.GetProperty("user").ValueKind.Should().Be(JsonValueKind.Null);
        bootstrap.GetProperty("locale").GetString().Should().Be("da-DK");
        bootstrap.GetProperty("runtimeConfiguration").EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(RuntimeConfigurationKeys);
        bootstrap.GetProperty("runtimeConfiguration").GetProperty("PUBLIC_URL").GetString().Should().Be("https://localhost");
        bootstrap.GetProperty("systemFeatureFlags").EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(SystemFeatureFlagKeys);
        bootstrap.GetProperty("antiforgeryToken").GetString().Should().NotBeNullOrEmpty();
        TelemetryEventsCollectorSpy.CollectedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBootstrap_WhenAuthenticatedAsOwner_ShouldReturnIdentityTenantAndFlagsWithoutTokens()
    {
        // Act
        var response = await AuthenticatedOwnerHttpClient.GetAsync(BootstrapUrl);

        // Assert
        response.ShouldBeSuccessfulGetRequest();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        var bootstrap = JsonDocument.Parse(body).RootElement;
        bootstrap.GetProperty("isAuthenticated").GetBoolean().Should().BeTrue();
        var user = bootstrap.GetProperty("user");
        user.GetProperty("id").GetString().Should().Be(DatabaseSeeder.Tenant1Owner.Id.ToString());
        user.GetProperty("tenantId").GetString().Should().Be(DatabaseSeeder.Tenant1.Id.ToString());
        user.GetProperty("email").GetString().Should().Be(DatabaseSeeder.Tenant1Owner.Email);
        user.GetProperty("role").GetString().Should().Be("Owner");
        user.GetProperty("featureFlags").ValueKind.Should().Be(JsonValueKind.Array);
        user.EnumerateObject().Select(property => property.Name).Should().NotContain(["sessionId", "accessToken", "refreshToken"]);
        body.Should().NotContain(AuthenticatedOwnerHttpClient.DefaultRequestHeaders.Authorization!.Parameter!);
        body.Should().NotContain(DatabaseSeeder.Tenant1OwnerSession.Id.ToString());
        response.Headers.TryGetValues("Set-Cookie", out var cookies);
        (cookies ?? []).Should().NotContain(cookie => cookie.StartsWith("__Host-access-token") || cookie.StartsWith("__Host-refresh-token"));
        response.Headers.Contains("x-access-token").Should().BeFalse();
        response.Headers.Contains("x-refresh-token").Should().BeFalse();
    }

    [Fact]
    public async Task GetBootstrap_WhenTwoUsersRequestAtTheSameTime_ShouldReturnOnlyEachUsersIdentity()
    {
        // Act
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).SelectMany(_ => new[]
                {
                    AuthenticatedOwnerHttpClient.GetStringAsync(BootstrapUrl).ContinueWith(task => (Expected: DatabaseSeeder.Tenant1Owner, Body: task.Result)),
                    AuthenticatedMemberHttpClient.GetStringAsync(BootstrapUrl).ContinueWith(task => (Expected: DatabaseSeeder.Tenant1Member, Body: task.Result))
                }
            )
        );

        // Assert
        foreach (var (expected, body) in responses)
        {
            var user = JsonDocument.Parse(body).RootElement.GetProperty("user");
            user.GetProperty("id").GetString().Should().Be(expected.Id.ToString());
            user.GetProperty("email").GetString().Should().Be(expected.Email);
            var other = expected == DatabaseSeeder.Tenant1Owner ? DatabaseSeeder.Tenant1Member : DatabaseSeeder.Tenant1Owner;
            body.Should().NotContain(other.Email).And.NotContain(other.Id.ToString());
        }
    }

    [Fact]
    public async Task GetBootstrap_WhenEnvironmentHasUndeclaredKeys_ShouldNeverExposeThem()
    {
        // Arrange
        var undeclaredPublicKey = $"PUBLIC_UNDECLARED_{Faker.Random.AlphaNumeric(8).ToUpperInvariant()}";
        var privateKey = $"PRIVATE_SETTING_{Faker.Random.AlphaNumeric(8).ToUpperInvariant()}";
        var secretValue = Faker.Random.AlphaNumeric(24);
        Environment.SetEnvironmentVariable(undeclaredPublicKey, secretValue);
        Environment.SetEnvironmentVariable(privateKey, secretValue);

        try
        {
            // Act
            var response = await AnonymousHttpClient.GetAsync(BootstrapUrl);

            // Assert
            response.ShouldBeSuccessfulGetRequest();
            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotContain(undeclaredPublicKey).And.NotContain(privateKey).And.NotContain(secretValue);
        }
        finally
        {
            Environment.SetEnvironmentVariable(undeclaredPublicKey, null);
            Environment.SetEnvironmentVariable(privateKey, null);
        }
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.c2lnbmF0dXJl")]
    public async Task GetBootstrap_WhenBearerTokenIsMalformed_ShouldReturnUnauthorizedInsteadOfAnonymous(string token)
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, BootstrapUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await AnonymousHttpClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await response.Content.ReadAsStringAsync()).Should().NotContain("antiforgeryToken");
    }

    [Fact]
    public async Task GetBootstrap_WhenDeserializedAsContract_ShouldRoundTripIdentity()
    {
        // Act
        var response = await AuthenticatedMemberHttpClient.GetAsync(BootstrapUrl);

        // Assert
        response.ShouldBeSuccessfulGetRequest();
        var bootstrap = await response.DeserializeResponse<BootstrapResponse>();
        bootstrap!.User!.Id.Should().Be(DatabaseSeeder.Tenant1Member.Id);
        bootstrap.User.TenantId.Should().Be(DatabaseSeeder.Tenant1.Id);
        bootstrap.SystemFeatureFlags.Keys.Should().BeEquivalentTo(SystemFeatureFlagKeys);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
