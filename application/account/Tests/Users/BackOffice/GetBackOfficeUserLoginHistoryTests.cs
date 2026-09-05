using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Account.Features.Authentication.Domain;
using Account.Features.EmailAuthentication.Domain;
using Account.Features.ExternalAuthentication.Domain;
using Account.Features.Users.BackOffice.Queries;
using Account.Tests.BackOffice;
using FluentAssertions;
using SharedKernel.Authentication.BackOfficeIdentity;
using SharedKernel.Authentication.MockEasyAuth;
using SharedKernel.Domain;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.Users.BackOffice;

public sealed class GetBackOfficeUserLoginHistoryTests(BackOfficeWebApplicationFactory factory) : BackOfficeEndpointBaseTest(factory), IClassFixture<BackOfficeWebApplicationFactory>
{
    [Fact]
    public async Task GetBackOfficeUserLoginHistory_WhenUserHasEmailAndExternalLogins_ShouldReturnUnion()
    {
        // Arrange
        var user = DatabaseSeeder.Tenant1Owner;
        SeedEmailLogin(user.Email, true, 0, 10);
        SeedEmailLogin(user.Email, false, EmailLogin.MaxAttempts, 60);
        SeedExternalLogin(user.Email, ExternalLoginResult.Success, 5);
        SeedExternalLogin(user.Email, ExternalLoginResult.IdentityProviderError, 120);
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "user");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.GetAsync($"/api/back-office/users/{user.Id}/login-history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<BackOfficeUserLoginHistoryResponse>();
        payload.Should().NotBeNull();
        payload.Entries.Should().HaveCount(4);
        payload.Entries.Should().Contain(e => e.Kind == LoginEventKind.Email && e.Outcome == LoginEventOutcome.Succeeded);
        payload.Entries.Should().Contain(e => e.Kind == LoginEventKind.Email && e.Outcome == LoginEventOutcome.Failed);
        payload.Entries.Should().Contain(e => e.Kind == LoginEventKind.External && e.Method == LoginMethod.Google && e.Outcome == LoginEventOutcome.Succeeded);
        payload.Entries.Should().Contain(e => e.Kind == LoginEventKind.External && e.Outcome == LoginEventOutcome.Failed && e.FailureReason == "IdentityProviderError");
    }

    [Fact]
    public async Task GetBackOfficeUserLoginHistory_WhenTheUserHasAMitIdLogin_ShouldReturnItWithoutDuplicatingTheOthers()
    {
        // A MitID login carries no email, because the provider vouches for none, so it is found by the account it
        // resolved instead. Before that lookup existed it was invisible here with no error anywhere.

        // Arrange
        var user = DatabaseSeeder.Tenant1Owner;
        SeedExternalLogin(user.Email, ExternalLoginResult.Success, 5);
        SeedExternalLogin(ExternalProviderType.MitId, null, user.Id, user.TenantId, ExternalLoginResult.Success, 2);
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "user");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.GetAsync($"/api/back-office/users/{user.Id}/login-history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<BackOfficeUserLoginHistoryResponse>();
        payload.Should().NotBeNull();
        payload.Entries.Should().HaveCount(2);
        payload.Entries.Should().ContainSingle(e => e.Method == LoginMethod.MitId && e.ExternalProvider == ExternalProviderType.MitId && e.Outcome == LoginEventOutcome.Succeeded);
        payload.Entries.Should().ContainSingle(e => e.Method == LoginMethod.Google);
    }

    [Fact]
    public async Task GetBackOfficeUserLoginHistory_WhenALoginMatchesBothByEmailAndByUser_ShouldReturnItOnce()
    {
        // A Google login records the account it resolved as well as the email it carried, so it is found by both
        // lookups. It must still appear once.

        // Arrange
        var user = DatabaseSeeder.Tenant1Owner;
        SeedExternalLogin(ExternalProviderType.Google, user.Email, user.Id, user.TenantId, ExternalLoginResult.Success, 5);
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "user");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.GetAsync($"/api/back-office/users/{user.Id}/login-history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<BackOfficeUserLoginHistoryResponse>();
        payload.Should().NotBeNull();
        payload.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task GetBackOfficeUserLoginHistory_WhenLoginsAreOlderThan30Days_ShouldNotReturnThem()
    {
        // Arrange
        var user = DatabaseSeeder.Tenant1Owner;
        SeedEmailLogin(user.Email, true, 0, 60 * 24 * 60); // 60 days
        SeedEmailLogin(user.Email, true, 0, 60 * 24 * 5); // 5 days
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "user");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.GetAsync($"/api/back-office/users/{user.Id}/login-history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<BackOfficeUserLoginHistoryResponse>();
        payload.Should().NotBeNull();
        payload.Entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetBackOfficeUserLoginHistory_WhenUserNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var unknownUserId = UserId.NewId();
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "user");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.GetAsync($"/api/back-office/users/{unknownUserId}/login-history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetBackOfficeUserLoginHistory_WhenCalledWithoutAuthentication_ShouldReturnUnauthorized()
    {
        // Arrange
        using var client = CreateBackOfficeClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Act
        var response = await client.GetAsync($"/api/back-office/users/{DatabaseSeeder.Tenant1Owner.Id}/login-history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBackOfficeUserLoginHistory_WhenCalledViaWrongHost_ShouldReturnNotFound()
    {
        // Arrange
        using var client = CreateClientForHost("app.test.localhost");
        client.DefaultRequestHeaders.Add(BackOfficeIdentityDefaults.PrincipalNameHeader, "Some User");

        // Act
        var response = await client.GetAsync($"/api/back-office/users/{DatabaseSeeder.Tenant1Owner.Id}/login-history");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private void SeedEmailLogin(string email, bool completed, int retryCount, int createdMinutesAgo)
    {
        Connection.Insert("email_logins", [
                ("id", EmailLoginId.NewId().ToString()),
                ("created_at", DateTimeOffset.UtcNow.AddMinutes(-createdMinutesAgo)),
                ("modified_at", null),
                ("type", nameof(EmailLoginType.Login)),
                ("email", email.ToLower()),
                ("one_time_password_hash", "hash"),
                ("retry_count", retryCount),
                ("resend_count", 0),
                ("completed", completed)
            ]
        );
    }

    private void SeedExternalLogin(string email, ExternalLoginResult? result, int createdMinutesAgo)
    {
        SeedExternalLogin(ExternalProviderType.Google, email, null, null, result, createdMinutesAgo);
    }

    private void SeedExternalLogin(ExternalProviderType providerType, string? email, UserId? userId, TenantId? tenantId, ExternalLoginResult? result, int createdMinutesAgo)
    {
        Connection.Insert("external_logins", [
                ("id", ExternalLoginId.NewId().ToString()),
                ("created_at", DateTimeOffset.UtcNow.AddMinutes(-createdMinutesAgo)),
                ("modified_at", null),
                ("type", nameof(ExternalLoginType.Login)),
                ("provider_type", providerType.ToString()),
                ("email", email?.ToLower()),
                ("code_verifier", "code-verifier"),
                ("nonce", "nonce"),
                ("browser_fingerprint", "fingerprint"),
                ("login_result", result?.ToString()),
                ("used_mock_provider", false),
                ("user_id", userId?.ToString()),
                ("tenant_id", tenantId?.ToString())
            ]
        );
    }
}
