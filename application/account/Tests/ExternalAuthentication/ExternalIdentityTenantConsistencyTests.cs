using Account.Features.ExternalAuthentication.Domain;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

// The schema under test is built from the Entity Framework model, so these tests prove that the composite foreign
// key declared in ExternalIdentityConfiguration exists and not just that the migration declares it. The invariant
// is that an identity row is scoped to the tenant of its own user; without it a row could name one tenant while
// its user belonged to another, and this table decides which account a person is logged into.
public sealed class ExternalIdentityTenantConsistencyTests : ExternalAuthenticationTestBase
{
    [Fact]
    public void InsertExternalIdentity_WhenTenantMatchesUserTenant_ShouldSucceed()
    {
        // Arrange
        var tenantId = InsertTenant();
        var userId = InsertUser(Faker.Internet.Email(), tenantId);

        // Act
        var insertIdentity = () => InsertExternalIdentity(userId, ExternalProviderType.Google, Faker.Random.AlphaNumeric(21), tenantId);

        // Assert
        insertIdentity.Should().NotThrow();
    }

    [Fact]
    public void InsertExternalIdentity_WhenTenantDiffersFromUserTenant_ShouldViolateForeignKey()
    {
        // Arrange
        var otherTenantId = InsertTenant();
        var userId = InsertUser(Faker.Internet.Email());

        // Act
        var insertIdentity = () => InsertExternalIdentity(userId, ExternalProviderType.Google, Faker.Random.AlphaNumeric(21), otherTenantId);

        // Assert
        insertIdentity.Should().Throw<SqliteException>().WithMessage("*FOREIGN KEY constraint failed*");
    }

    [Fact]
    public void InsertExternalIdentity_WhenSameProviderIdentityInAnotherTenant_ShouldSucceed()
    {
        // Arrange
        var providerUserId = Faker.Random.AlphaNumeric(21);
        var otherTenantId = InsertTenant();
        var userId = InsertUser(Faker.Internet.Email());
        var otherTenantUserId = InsertUser(Faker.Internet.Email(), otherTenantId);
        InsertExternalIdentity(userId, ExternalProviderType.Google, providerUserId);

        // Act
        var insertSecondIdentity = () => InsertExternalIdentity(otherTenantUserId, ExternalProviderType.Google, providerUserId, otherTenantId);

        // Assert
        insertSecondIdentity.Should().NotThrow();
        CountExternalIdentities(ExternalProviderType.Google, providerUserId).Should().Be(1);
        CountExternalIdentities(ExternalProviderType.Google, providerUserId, otherTenantId).Should().Be(1);
    }

    [Fact]
    public void DeleteUser_WhenUserIsHardDeleted_ShouldCascadeToExternalIdentity()
    {
        // Arrange
        var providerUserId = Faker.Random.AlphaNumeric(21);
        var userId = InsertUser(Faker.Internet.Email());
        InsertExternalIdentity(userId, ExternalProviderType.Google, providerUserId);

        // Act
        Connection.Delete("users", userId.ToString());

        // Assert
        CountExternalIdentities(ExternalProviderType.Google, providerUserId).Should().Be(0);
    }
}
