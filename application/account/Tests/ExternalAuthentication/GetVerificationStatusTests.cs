using System.Net;
using System.Text.Json;
using Account.Features.ExternalAuthentication.Domain;
using Account.Features.ExternalAuthentication.Queries;
using FluentAssertions;
using SharedKernel.Domain;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

public sealed class GetVerificationStatusTests : ExternalAuthenticationTestBase
{
    private const string VerificationStatusPath = "/api/account/authentication/verification";
    private const string PersonIdentifier = "mitid-person-identifier";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task GetVerificationStatus_WhenTheUserHasNeverVerified_ShouldReportNotVerified()
    {
        // Act
        var response = await AuthenticatedOwnerHttpClient.GetAsync(VerificationStatusPath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await ReadStatus(response);
        status.IsVerified.Should().BeFalse();
        status.Provider.Should().BeNull();
        status.AssuranceLevel.Should().BeNull();
        status.VerifiedAt.Should().BeNull();
        status.AuthenticatedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetVerificationStatus_WhenTheUserIsVerified_ShouldReportTheEvidenceWithoutTheIdentifier()
    {
        // Arrange
        var verifiedAt = DateTimeOffset.UtcNow.AddDays(-1);
        SeedVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id, ExternalProviderType.MitId, verifiedAt);

        // Act
        var response = await AuthenticatedOwnerHttpClient.GetAsync(VerificationStatusPath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(PersonIdentifier);
        var status = JsonSerializer.Deserialize<VerificationStatusResponse>(body, JsonOptions)!;
        status.IsVerified.Should().BeTrue();
        status.Provider.Should().Be(ExternalProviderType.MitId);
        status.AssuranceLevel.Should().Be(IdentityAssuranceLevel.Substantial);
        status.VerifiedAt.Should().BeCloseTo(verifiedAt, TimeSpan.FromSeconds(1));
        status.AuthenticatedAt.Should().BeCloseTo(verifiedAt.AddSeconds(-2), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetVerificationStatus_WhenAnotherUserInTheTenantIsVerified_ShouldReportOnlyTheCallersOwnStatus()
    {
        // Arrange
        SeedVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id, ExternalProviderType.MitId, DateTimeOffset.UtcNow.AddDays(-1));

        // Act
        var response = await AuthenticatedMemberHttpClient.GetAsync(VerificationStatusPath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await ReadStatus(response);
        status.IsVerified.Should().BeFalse();
        status.Provider.Should().BeNull();
    }

    [Fact]
    public async Task GetVerificationStatus_WhenTheUserHoldsSeveralVerifiedIdentities_ShouldReportTheMostRecent()
    {
        // Arrange
        SeedVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id, ExternalProviderType.MitId, DateTimeOffset.UtcNow.AddDays(-3));
        SeedVerifiedIdentity(DatabaseSeeder.Tenant1Owner.Id, ExternalProviderType.Google, DateTimeOffset.UtcNow.AddDays(-1), "Login, Verification");

        // Act
        var response = await AuthenticatedOwnerHttpClient.GetAsync(VerificationStatusPath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await ReadStatus(response);
        status.IsVerified.Should().BeTrue();
        status.Provider.Should().Be(ExternalProviderType.Google);
    }

    [Fact]
    public async Task GetVerificationStatus_WhenAnonymous_ShouldReturnUnauthorized()
    {
        // Act
        var response = await NoRedirectHttpClient.GetAsync(VerificationStatusPath);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<VerificationStatusResponse> ReadStatus(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<VerificationStatusResponse>(body, JsonOptions)!;
    }

    private void SeedVerifiedIdentity(UserId userId, ExternalProviderType provider, DateTimeOffset verifiedAt, string capabilities = nameof(ExternalIdentityCapabilities.Verification))
    {
        Connection.Insert("external_identities", [
                ("tenant_id", DatabaseSeeder.Tenant1.Id.ToString()),
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
    }
}
