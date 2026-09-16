using System.Net;
using FluentAssertions;
using PublicClientSpike.Tests.Harness;
using SharedKernel.Authentication;
using SharedKernel.Tests.Persistence;

namespace PublicClientSpike.Tests;

// SPIKE CODE (T012). Session lifecycle of a cookie-free native client, with antiforgery validation active on every write.
public sealed class LifecycleTests(PublicClientSpikeFactory factory) : PublicClientSpikeTest(factory)
{
    private const string AccessTokenLifetimeBound = "the access token is a stateless JWT valid for up to 5 minutes after revocation";

    [Fact]
    public async Task Refresh_WhenTwoRequestsRaceWithTheSameToken_ShouldBothSucceedAndATokenTwoVersionsOldShouldRevokeTheSession()
    {
        // Arrange
        var tokens = await Harness.SignInAsync(OwnerWebAccessToken());

        // Act
        var concurrent = await Task.WhenAll(Harness.RefreshAsync(tokens.RefreshToken), Harness.RefreshAsync(tokens.RefreshToken));
        var version2 = await NativeClientHarness.ReadTokensAsync(concurrent[0]);
        await NativeClientHarness.ReadTokensAsync(concurrent[1]);
        var version3 = await Harness.RefreshTokensAsync(version2.RefreshToken);
        var replay = await Harness.RefreshAsync(tokens.RefreshToken);
        var afterReplay = await Harness.RefreshAsync(version3.RefreshToken);

        // Assert
        concurrent.Select(response => response.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.OK);
        Claim(version3.RefreshToken, "ver").Should().Be("3");
        (await ReadJsonAsync(replay)).GetProperty("error_description").GetString().Should().Be(nameof(UnauthorizedReason.ReplayAttackDetected));
        (await ReadJsonAsync(afterReplay)).GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Logout_WhenCookieFreeClientLogsOut_ShouldRevokeTheNativeSessionAndRejectRefresh()
    {
        // Arrange
        var tokens = await Harness.SignInAsync(OwnerWebAccessToken());

        // Act
        var logout = await Harness.WriteAsync(tokens.AccessToken, HttpMethod.Post, "/api/account/authentication/logout");
        var refresh = await Harness.RefreshAsync(tokens.RefreshToken);
        using var client = Harness.CreateClient(tokens.AccessToken);
        var readWithOldAccessToken = await client.GetAsync("/api/account/users/me");

        // Assert
        logout.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(refresh)).GetProperty("error_description").GetString().Should().Be("LoggedOut");
        Connection.ExecuteScalar<string>("SELECT revoked_reason FROM sessions WHERE id = @id", [new { id = tokens.SessionId }]).Should().Be("LoggedOut");
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sessions WHERE id = @id AND revoked_at IS NULL", [new { id = DatabaseSeeder.Tenant1OwnerSession.Id.ToString() }]).Should().Be(1);
        readWithOldAccessToken.StatusCode.Should().Be(HttpStatusCode.OK, AccessTokenLifetimeBound);
    }

    [Fact]
    public async Task RevokeSession_WhenWebSessionRevokesTheNativeSession_ShouldRejectRefresh()
    {
        // Arrange
        var tokens = await Harness.SignInAsync(OwnerWebAccessToken());

        // Act
        var revoke = await Harness.WriteAsync(OwnerWebAccessToken(), HttpMethod.Delete, $"/api/account/authentication/sessions/{tokens.SessionId}");
        var refresh = await Harness.RefreshAsync(tokens.RefreshToken);

        // Assert
        revoke.IsSuccessStatusCode.Should().BeTrue();
        (await ReadJsonAsync(refresh)).GetProperty("error_description").GetString().Should().Be("Revoked");
    }

    [Fact]
    public async Task SwitchTenant_WhenCookieFreeClientSwitches_ShouldReturnReplacementTokensThatWorkInTheNewTenantAndRevokeTheOldOnes()
    {
        // Arrange
        var (tenant2Id, tenant2UserId) = InsertSecondTenantMembership(DatabaseSeeder.Tenant1Owner.Email);
        var tokens = await Harness.SignInAsync(OwnerWebAccessToken());

        // Act
        var switchTenant = await Harness.WriteAsync(tokens.AccessToken, HttpMethod.Post, "/api/account/authentication/switch-tenant", new { tenantId = tenant2Id });
        var replacementAccessToken = switchTenant.Headers.GetValues(AuthenticationTokenHttpKeys.AccessTokenHttpHeaderKey).Single();
        var replacementRefreshToken = switchTenant.Headers.GetValues(AuthenticationTokenHttpKeys.RefreshTokenHttpHeaderKey).Single();
        using var replacementClient = Harness.CreateClient(replacementAccessToken);
        var bootstrap = await replacementClient.GetAsync("/api/account/bootstrap");
        var me = await replacementClient.GetAsync("/api/account/users/me");
        var tenant1UserRead = await replacementClient.GetAsync($"/api/account/users/{DatabaseSeeder.Tenant1Member.Id}");
        var refreshedReplacement = await Harness.RefreshTokensAsync(replacementRefreshToken);
        var writeInNewTenant = await Harness.WriteAsync(refreshedReplacement.AccessToken, HttpMethod.Put, "/api/account/tenants/current", new { name = "Renamed second" });
        var oldRefresh = await Harness.RefreshAsync(tokens.RefreshToken);

        // Assert
        switchTenant.StatusCode.Should().Be(HttpStatusCode.OK);
        switchTenant.Headers.Contains("Set-Cookie").Should().BeFalse("the account API returns replacement tokens as headers; only the gateway turns them into cookies");
        Claim(replacementAccessToken, "tenant_id").Should().Be(tenant2Id.ToString());
        bootstrap.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(bootstrap)).GetRawText().Should().Contain(tenant2Id.ToString());
        (await ReadJsonAsync(me)).GetProperty("id").GetString().Should().Be(tenant2UserId.ToString());
        tenant1UserRead.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Claim(refreshedReplacement.AccessToken, "tenant_id").Should().Be(tenant2Id.ToString());
        writeInNewTenant.StatusCode.Should().Be(HttpStatusCode.OK);
        Connection.ExecuteScalar<string>("SELECT name FROM tenants WHERE id = @id", [new { id = tenant2Id.Value }]).Should().Be("Renamed second");
        (await ReadJsonAsync(oldRefresh)).GetProperty("error_description").GetString().Should().Be("SwitchTenant");
    }

    [Fact]
    public async Task ChangeUserRole_WhenOwnerPromotesMember_ShouldAppearInTheMembersNextRefreshedAccessToken()
    {
        // Arrange
        var memberTokens = await Harness.SignInAsync(MemberWebAccessToken());
        var ownerTokens = await Harness.SignInAsync(OwnerWebAccessToken());

        // Act
        var changeRole = await Harness.WriteAsync(ownerTokens.AccessToken, HttpMethod.Put, $"/api/account/users/{DatabaseSeeder.Tenant1Member.Id}/change-user-role", new { userRole = "Admin" });
        var refreshed = await Harness.RefreshTokensAsync(memberTokens.RefreshToken);

        // Assert
        changeRole.IsSuccessStatusCode.Should().BeTrue();
        Claim(memberTokens.AccessToken, System.Security.Claims.ClaimTypes.Role).Should().Be("Member");
        Claim(refreshed.AccessToken, System.Security.Claims.ClaimTypes.Role).Should().Be("Admin");
        Claim(refreshed.AccessToken, AuthenticationTokenHttpKeys.FeatureFlagsClaimName).Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateCurrentUserAndTenant_WhenCookieFreeClientWrites_ShouldSucceedAndAppearAfterRefresh()
    {
        // Arrange
        var tokens = await Harness.SignInAsync(OwnerWebAccessToken());

        // Act
        var updateUser = await Harness.WriteAsync(tokens.AccessToken, HttpMethod.Put, "/api/account/users/me", new { firstName = "Native", lastName = "Client", title = "Spike" });
        var updateTenant = await Harness.WriteAsync(tokens.AccessToken, HttpMethod.Put, "/api/account/tenants/current", new { name = "Native tenant" });
        var withoutAntiforgery = await Harness.CreateClient(tokens.AccessToken).PutAsync("/api/account/tenants/current", System.Net.Http.Json.JsonContent.Create(new { name = "Blocked" }));
        var refreshed = await Harness.RefreshTokensAsync(tokens.RefreshToken);

        // Assert
        updateUser.IsSuccessStatusCode.Should().BeTrue();
        updateTenant.IsSuccessStatusCode.Should().BeTrue();
        withoutAntiforgery.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Claim(refreshed.AccessToken, "given_name").Should().Be("Native");
        Claim(refreshed.AccessToken, "tenant_name").Should().Be("Native tenant");
    }

    [Fact]
    public async Task Read_WhenNativeTokenAddressesAnotherTenantsUser_ShouldReturnNotFound()
    {
        // Arrange
        var (_, tenant2UserId) = InsertSecondTenantMembership("someone@tenant-2.example");
        var tokens = await Harness.SignInAsync(OwnerWebAccessToken());
        using var client = Harness.CreateClient(tokens.AccessToken);

        // Act
        var response = await client.GetAsync($"/api/account/users/{tenant2UserId}");
        var write = await Harness.WriteAsync(tokens.AccessToken, HttpMethod.Put, $"/api/account/users/{tenant2UserId}/change-user-role", new { userRole = "Member" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        write.IsSuccessStatusCode.Should().BeFalse();
    }
}
