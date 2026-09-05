using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Account.Features.ExternalAuthentication.BackOffice.Queries;
using Account.Features.ExternalAuthentication.Domain;
using Account.Tests.BackOffice;
using FluentAssertions;
using SharedKernel.Authentication.MockEasyAuth;
using SharedKernel.Domain;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.ExternalAuthentication.BackOffice;

public sealed class GetBackOfficeUserIdentityVerificationTests(BackOfficeWebApplicationFactory factory) : BackOfficeEndpointBaseTest(factory), IClassFixture<BackOfficeWebApplicationFactory>
{
    private const string PersonIdentifier = "mitid-person-identifier";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task GetBackOfficeUserIdentityVerification_WhenTheUserIsVerified_ShouldReturnTheEvidence()
    {
        // Arrange
        var verifiedAt = SeedVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id, DatabaseSeeder.Tenant1Owner.TenantId);
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "admin");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.GetAsync($"/api/back-office/users/{DatabaseSeeder.Tenant1Owner.Id}/identity-verification");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var verification = await ReadVerification(response);
        verification.IsVerified.Should().BeTrue();
        verification.Provider.Should().Be(ExternalProviderType.MitId);
        verification.AssuranceLevel.Should().Be(IdentityAssuranceLevel.Substantial);
        verification.VerifiedAt.Should().BeCloseTo(verifiedAt, TimeSpan.FromSeconds(1));
        verification.AuthenticatedAt.Should().BeCloseTo(verifiedAt.AddSeconds(-2), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetBackOfficeUserIdentityVerification_WhenTheUserIsVerified_ShouldNotExposeTheProviderIdentifier()
    {
        // A MitID Person-ID identifies a real person. An administrator needs to know a verification exists and how
        // strong it is, never the identifier, so this pins that it cannot leak through this endpoint.

        // Arrange
        SeedVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id, DatabaseSeeder.Tenant1Owner.TenantId);
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "admin");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.GetAsync($"/api/back-office/users/{DatabaseSeeder.Tenant1Owner.Id}/identity-verification");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(PersonIdentifier);
        body.Should().NotContain("broker-pseudonym");
    }

    [Fact]
    public async Task GetBackOfficeUserIdentityVerification_WhenTheUserHasNeverVerified_ShouldReportNotVerified()
    {
        // Arrange
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "admin");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.GetAsync($"/api/back-office/users/{DatabaseSeeder.Tenant1Owner.Id}/identity-verification");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var verification = await ReadVerification(response);
        verification.IsVerified.Should().BeFalse();
        verification.Provider.Should().BeNull();
        verification.AssuranceLevel.Should().BeNull();
        verification.VerifiedAt.Should().BeNull();
        verification.AuthenticatedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetBackOfficeUserIdentityVerification_WhenTheUserHoldsSeveralVerifiedIdentities_ShouldReturnTheMostRecent()
    {
        // Arrange
        SeedVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id, DatabaseSeeder.Tenant1Owner.TenantId, provider: ExternalProviderType.MitId, ageInDays: 3);
        SeedVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id, DatabaseSeeder.Tenant1Owner.TenantId, provider: ExternalProviderType.Google, ageInDays: 1);
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "admin");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.GetAsync($"/api/back-office/users/{DatabaseSeeder.Tenant1Owner.Id}/identity-verification");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var verification = await ReadVerification(response);
        verification.IsVerified.Should().BeTrue();
        verification.Provider.Should().Be(ExternalProviderType.Google);
    }

    [Fact]
    public async Task GetBackOfficeUserIdentityVerification_WhenNonAdminBackOfficeIdentity_ShouldReturnTheEvidence()
    {
        // Reading is not admin gated, matching the sibling sessions and login history reads on the same page. Only
        // the revoke that acts on this data requires an administrator.

        // Arrange
        SeedVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id, DatabaseSeeder.Tenant1Owner.TenantId);
        var identity = MockEasyAuthIdentities.Default.Single(i => i.Id == "user");
        using var client = CreateBackOfficeClientForIdentity(identity);

        // Act
        var response = await client.GetAsync($"/api/back-office/users/{DatabaseSeeder.Tenant1Owner.Id}/identity-verification");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var verification = await ReadVerification(response);
        verification.IsVerified.Should().BeTrue();
    }

    [Fact]
    public async Task GetBackOfficeUserIdentityVerification_WhenCalledWithoutAuthentication_ShouldReturnUnauthorized()
    {
        // Arrange
        using var client = CreateBackOfficeClient();

        // Act
        var response = await client.GetAsync($"/api/back-office/users/{DatabaseSeeder.Tenant1Owner.Id}/identity-verification");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<BackOfficeUserIdentityVerificationResponse> ReadVerification(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<BackOfficeUserIdentityVerificationResponse>(body, JsonOptions)!;
    }

    private DateTimeOffset SeedVerifiedIdentity(
        UserId userId,
        TenantId tenantId,
        string capabilities = nameof(ExternalIdentityCapabilities.Verification),
        ExternalProviderType provider = ExternalProviderType.MitId,
        int ageInDays = 1
    )
    {
        var verifiedAt = DateTimeOffset.UtcNow.AddDays(-ageInDays);

        Connection.Insert("external_identities", [
                ("tenant_id", tenantId.ToString()),
                ("id", ExternalIdentityId.NewId().ToString()),
                ("user_id", userId.ToString()),
                ("created_at", verifiedAt),
                ("modified_at", null),
                ("provider", provider.ToString()),
                ("provider_user_id", $"{provider.ToString().ToLowerInvariant()}-{PersonIdentifier}"),
                ("capabilities", capabilities),
                ("issuer", "https://tenant.idura.broker"),
                ("subject", "broker-pseudonym"),
                ("assurance_level", nameof(IdentityAssuranceLevel.Substantial)),
                ("verified_at", verifiedAt),
                ("authenticated_at", verifiedAt.AddSeconds(-2)),
                ("verified_by_external_login_id", ExternalLoginId.NewId().ToString())
            ]
        );

        return verifiedAt;
    }
}
