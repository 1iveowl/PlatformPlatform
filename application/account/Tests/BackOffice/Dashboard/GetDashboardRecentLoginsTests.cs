using System.Net;
using System.Net.Http.Json;
using Account.Features.Authentication.Domain;
using Account.Features.BackOffice.Dashboard.Queries;
using Account.Features.ExternalAuthentication.Domain;
using FluentAssertions;
using SharedKernel.Authentication.MockEasyAuth;
using SharedKernel.Domain;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.BackOffice.Dashboard;

public sealed class GetDashboardRecentLoginsTests(BackOfficeWebApplicationFactory factory) : BackOfficeEndpointBaseTest(factory), IClassFixture<BackOfficeWebApplicationFactory>
{
    [Fact]
    public async Task GetDashboardRecentLogins_WhenAMitIdLoginSucceeded_ShouldListIt()
    {
        // The mapping from provider to login method returns null for a provider with no login method and the caller
        // drops those rows rather than throwing, so a missing MitID arm would make this login vanish from the
        // dashboard with no error anywhere. Nothing but this test would reveal it.

        // Arrange
        var user = DatabaseSeeder.Tenant1Owner;
        SeedExternalLogin(ExternalProviderType.MitId, null, user.Id, user.TenantId, ExternalLoginResult.Success);
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "user");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.GetAsync("/api/back-office/dashboard/recent-logins?Limit=50");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<BackOfficeDashboardRecentLoginsResponse>();
        payload.Should().NotBeNull();

        var mitIdLogin = payload.Logins.Should().ContainSingle(l => l.Method == LoginMethod.MitId).Subject;

        // The row carries no email of its own, so the account it resolved is what names the person
        mitIdLogin.UserId.Should().Be(user.Id);
        mitIdLogin.Email.Should().Be(user.Email);
        mitIdLogin.TenantId.Should().Be(user.TenantId);
    }

    [Fact]
    public async Task GetDashboardRecentLogins_WhenAGoogleLoginSucceeded_ShouldStillListItByEmail()
    {
        // Arrange
        var user = DatabaseSeeder.Tenant1Owner;
        SeedExternalLogin(ExternalProviderType.Google, user.Email, null, null, ExternalLoginResult.Success);
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "user");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.GetAsync("/api/back-office/dashboard/recent-logins?Limit=50");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<BackOfficeDashboardRecentLoginsResponse>();
        payload.Should().NotBeNull();

        var googleLogin = payload.Logins.Should().ContainSingle(l => l.Method == LoginMethod.Google).Subject;
        googleLogin.Email.Should().Be(user.Email);
        googleLogin.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetDashboardRecentLogins_WhenAVerificationSucceeded_ShouldNotListIt()
    {
        // A verification completes the same way a login does but signs nobody in, so counting it would overstate
        // sign-in activity.

        // Arrange
        var user = DatabaseSeeder.Tenant1Owner;
        SeedExternalLogin(ExternalProviderType.MitId, null, user.Id, user.TenantId, ExternalLoginResult.Success, ExternalLoginType.Verification);
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "user");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.GetAsync("/api/back-office/dashboard/recent-logins?Limit=50");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<BackOfficeDashboardRecentLoginsResponse>();
        payload.Should().NotBeNull();
        payload.Logins.Should().NotContain(l => l.Method == LoginMethod.MitId);
    }

    private void SeedExternalLogin(
        ExternalProviderType providerType,
        string? email,
        UserId? userId,
        TenantId? tenantId,
        ExternalLoginResult result,
        ExternalLoginType loginType = ExternalLoginType.Login
    )
    {
        Connection.Insert("external_logins", [
                ("id", ExternalLoginId.NewId().ToString()),
                ("created_at", DateTimeOffset.UtcNow.AddMinutes(-5)),
                ("modified_at", null),
                ("type", loginType.ToString()),
                ("provider_type", providerType.ToString()),
                ("email", email?.ToLower()),
                ("code_verifier", "code-verifier"),
                ("nonce", "nonce"),
                ("browser_fingerprint", "fingerprint"),
                ("login_result", result.ToString()),
                ("used_mock_provider", false),
                ("user_id", userId?.ToString()),
                ("tenant_id", tenantId?.ToString())
            ]
        );
    }
}
