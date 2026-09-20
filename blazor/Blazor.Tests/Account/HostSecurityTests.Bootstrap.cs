using System.Security.Claims;
using System.Text.Json;
using Account.Features.Authentication.Queries;
using Blazor.Client.Bootstrap;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Blazor.Tests.Account;

// The host's prerender adapter must produce the account API's bootstrap contract: the same type, the same allowlisted
// configuration keys, identity from the access token's claims and no credentials. Part of HostSecurityTests so it shares
// the one host the fixture starts; the fixture sets process-wide environment variables before building it.
public sealed partial class HostSecurityTests
{
    private static readonly string[] BootstrapRuntimeConfigurationKeys =
    [
        "PUBLIC_URL", "CDN_URL", "APPLICATION_VERSION", "PUBLIC_PUSH_PUBLIC_KEY", "PUBLIC_GOOGLE_OAUTH_ENABLED",
        "PUBLIC_ENTRA_OAUTH_ENABLED", "PUBLIC_MITID_VERIFICATION_ENABLED", "PUBLIC_MITID_LOGIN_ENABLED",
        "PUBLIC_SUBSCRIPTION_ENABLED", "PUBLIC_PUSH_NOTIFICATIONS_ENABLED"
    ];

    private static readonly string[] SystemFeatureFlagKeys = ["google-oauth", "entra-oauth", "mitid-verification", "mitid-login", "subscriptions", "push-notifications-enabled"];

    [Fact]
    public async Task BootstrapAdapter_WhenAuthenticated_ShouldMapTokenClaimsToContractWithoutCredentials()
    {
        // Arrange
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "usr_01JZ8Q4N6V3K2M7P9R5T0W1XYZ"),
                    new Claim(ClaimTypes.Email, "someone@platformplatform.net"),
                    new Claim(ClaimTypes.GivenName, "Some"),
                    new Claim(ClaimTypes.Surname, string.Empty),
                    new Claim(ClaimTypes.Role, "Owner"),
                    new Claim("tenant_id", HostFixture.TenantIdClaimValue),
                    new Claim("tenant_name", "Tenant name"),
                    new Claim("session_id", "sess_01JZ8Q4N6V3K2M7P9R5T0W1XYA"),
                    new Claim("locale", "da-DK"),
                    new Claim("feature_flags", "sso,beta-features")
                ], "Bearer"
            )
        );

        // Act
        var (bootstrap, json) = await GetBootstrapFromAdapterAsync(principal);

        // Assert
        bootstrap.IsAuthenticated.Should().BeTrue();
        bootstrap.Locale.Should().Be("da-DK");
        bootstrap.User!.Id.Value.Should().Be("usr_01JZ8Q4N6V3K2M7P9R5T0W1XYZ");
        bootstrap.User.TenantId.Value.Should().Be(long.Parse(HostFixture.TenantIdClaimValue));
        bootstrap.User.TenantName.Should().Be("Tenant name");
        bootstrap.User.FirstName.Should().Be("Some");
        bootstrap.User.LastName.Should().BeNull();
        bootstrap.User.IsInternalUser.Should().BeTrue();
        bootstrap.User.FeatureFlags.Should().Equal("beta-features", "sso");
        bootstrap.AntiforgeryToken.Should().NotBeNullOrEmpty();
        json.Should().NotContain("sess_01JZ8Q4N6V3K2M7P9R5T0W1XYA");
    }

    [Fact]
    public async Task BootstrapAdapter_WhenEnvironmentHasUndeclaredPublicKey_ShouldExposeOnlyAllowlistedConfiguration()
    {
        // Arrange
        var undeclaredKey = $"PUBLIC_UNDECLARED_{Guid.NewGuid():N}".ToUpperInvariant();
        Environment.SetEnvironmentVariable(undeclaredKey, "leak");

        try
        {
            // Act
            var (bootstrap, json) = await GetBootstrapFromAdapterAsync(new ClaimsPrincipal(new ClaimsIdentity()));

            // Assert
            bootstrap.IsAuthenticated.Should().BeFalse();
            bootstrap.User.Should().BeNull();
            bootstrap.RuntimeConfiguration.Keys.Should().BeEquivalentTo(BootstrapRuntimeConfigurationKeys);
            bootstrap.SystemFeatureFlags.Keys.Should().BeEquivalentTo(SystemFeatureFlagKeys);
            json.Should().NotContain(undeclaredKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable(undeclaredKey, null);
        }
    }

    private async Task<(BootstrapResponse Bootstrap, string Json)> GetBootstrapFromAdapterAsync(ClaimsPrincipal principal)
    {
        await using var scope = fixture.HostServices.CreateAsyncScope();
        var context = new DefaultHttpContext { User = principal, RequestServices = scope.ServiceProvider, Request = { Scheme = "https", Host = new HostString(HostFixture.PublicHost) } };
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;

        var bootstrap = await scope.ServiceProvider.GetRequiredService<IBootstrapSource>().GetAsync();
        var json = JsonSerializer.Serialize(bootstrap, JsonSerializerOptions.Web);
        return (JsonSerializer.Deserialize<BootstrapResponse>(json, JsonSerializerOptions.Web)!, json);
    }
}
