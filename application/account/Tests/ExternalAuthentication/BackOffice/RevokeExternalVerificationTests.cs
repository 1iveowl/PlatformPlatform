using System.Net;
using Account.Features.ExternalAuthentication.Domain;
using Account.Tests.BackOffice;
using FluentAssertions;
using SharedKernel.Authentication.MockEasyAuth;
using SharedKernel.Domain;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.ExternalAuthentication.BackOffice;

public sealed class RevokeExternalVerificationTests(BackOfficeWebApplicationFactory factory) : BackOfficeEndpointBaseTest(factory), IClassFixture<BackOfficeWebApplicationFactory>
{
    [Fact]
    public async Task RevokeExternalVerification_WhenTheIdentityOnlyEverVerified_ShouldRemoveTheRowSoAnotherIdentityCanBeBound()
    {
        // Arrange
        var externalIdentityId = SeedVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id, DatabaseSeeder.Tenant1Owner.TenantId);
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "admin");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.DeleteAsync($"/api/back-office/users/{DatabaseSeeder.Tenant1Owner.Id}/identity-verification");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The row is gone, not merely cleared. A retained row would still hold the unique index on user and provider
        // and still name the old identity, so the next verification with a different MitID would fail as a mismatch,
        // which is the lock-out this command exists to clear.
        Connection.RowExists("external_identities", externalIdentityId).Should().BeFalse();

        TelemetryEventsCollectorSpy.CollectedEvents.Should().ContainSingle(e => e.GetType().Name == "ExternalVerificationRevoked");
    }

    [Fact]
    public async Task RevokeExternalVerification_WhenTheIdentityCouldAlsoLogIn_ShouldRemoveTheRowAndEndMitIdLogin()
    {
        // The right to log in came from the verification, so withdrawing the verification withdraws it too and the
        // row is left with nothing. Keeping it would leave the account holding a working credential for the identity
        // an administrator had just withdrawn, and would block the person from ever verifying with a different one.

        // Arrange
        var externalIdentityId = SeedVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id, DatabaseSeeder.Tenant1Owner.TenantId, $"{nameof(ExternalIdentityCapabilities.Login)}, {nameof(ExternalIdentityCapabilities.Verification)}");
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "admin");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.DeleteAsync($"/api/back-office/users/{DatabaseSeeder.Tenant1Owner.Id}/identity-verification");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Connection.RowExists("external_identities", externalIdentityId).Should().BeFalse();
    }

    [Fact]
    public async Task RevokeExternalVerification_WhenTheUserAlsoHasAGoogleIdentity_ShouldLeaveItUntouched()
    {
        // Revoking withdraws a verification, and only MitID can be verified. An identity the person logs in with for
        // an unrelated provider is none of this command's business.

        // Arrange
        var mitIdIdentityId = SeedVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id, DatabaseSeeder.Tenant1Owner.TenantId, $"{nameof(ExternalIdentityCapabilities.Login)}, {nameof(ExternalIdentityCapabilities.Verification)}");
        var googleIdentityId = SeedGoogleLoginIdentity(DatabaseSeeder.Tenant1Owner.Id, DatabaseSeeder.Tenant1Owner.TenantId);
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "admin");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.DeleteAsync($"/api/back-office/users/{DatabaseSeeder.Tenant1Owner.Id}/identity-verification");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Connection.RowExists("external_identities", mitIdIdentityId).Should().BeFalse();
        Connection.RowExists("external_identities", googleIdentityId).Should().BeTrue();
        Connection.ExecuteScalar<string>("SELECT capabilities FROM external_identities WHERE id = @id", [new { id = googleIdentityId }])
            .Should().Be(nameof(ExternalIdentityCapabilities.Login));
    }

    [Fact]
    public async Task RevokeExternalVerification_WhenTheUserHasNoVerifiedIdentity_ShouldReturnNotFound()
    {
        // Arrange
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "admin");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.DeleteAsync($"/api/back-office/users/{DatabaseSeeder.Tenant1Owner.Id}/identity-verification");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RevokeExternalVerification_WhenNonAdminBackOfficeIdentity_ShouldReturnForbidden()
    {
        // Arrange
        SeedVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id, DatabaseSeeder.Tenant1Owner.TenantId);
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "user");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.DeleteAsync($"/api/back-office/users/{DatabaseSeeder.Tenant1Owner.Id}/identity-verification");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RevokeExternalVerification_WhenUnauthenticated_ShouldReturnUnauthorized()
    {
        // Arrange
        using var client = CreateBackOfficeClient();

        // Act
        var response = await client.DeleteAsync($"/api/back-office/users/{DatabaseSeeder.Tenant1Owner.Id}/identity-verification");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private string SeedGoogleLoginIdentity(UserId userId, TenantId tenantId)
    {
        var externalIdentityId = ExternalIdentityId.NewId().ToString();

        Connection.Insert("external_identities", [
                ("tenant_id", tenantId.ToString()),
                ("id", externalIdentityId),
                ("user_id", userId.ToString()),
                ("created_at", DateTimeOffset.UtcNow.AddDays(-1)),
                ("modified_at", null),
                ("provider", nameof(ExternalProviderType.Google)),
                ("provider_user_id", "google-user-id-123"),
                ("capabilities", nameof(ExternalIdentityCapabilities.Login)),
                ("issuer", "https://accounts.google.com"),
                ("subject", "google-user-id-123")
            ]
        );

        return externalIdentityId;
    }

    private string SeedVerifiedIdentity(UserId userId, TenantId tenantId, string capabilities = nameof(ExternalIdentityCapabilities.Verification))
    {
        var externalIdentityId = ExternalIdentityId.NewId().ToString();
        var verifiedAt = DateTimeOffset.UtcNow.AddDays(-1);

        Connection.Insert("external_identities", [
                ("tenant_id", tenantId.ToString()),
                ("id", externalIdentityId),
                ("user_id", userId.ToString()),
                ("created_at", verifiedAt),
                ("modified_at", null),
                ("provider", nameof(ExternalProviderType.MitId)),
                ("provider_user_id", "mitid-person-identifier"),
                ("capabilities", capabilities),
                ("issuer", "https://tenant.idura.broker"),
                ("subject", "broker-pseudonym"),
                ("assurance_level", nameof(IdentityAssuranceLevel.Substantial)),
                ("verified_at", verifiedAt),
                ("authenticated_at", verifiedAt),
                ("verified_by_external_login_id", ExternalLoginId.NewId().ToString())
            ]
        );

        return externalIdentityId;
    }
}
