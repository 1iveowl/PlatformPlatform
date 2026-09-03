using Account.Features.ExternalAuthentication.Domain;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

// The schema under test is built from the Entity Framework model, so these tests prove that the two unique
// indexes declared in ExternalIdentityConfiguration exist and not just that the migration declares them
public sealed class ExternalIdentityUniqueIndexTests : ExternalAuthenticationTestBase
{
    [Fact]
    public void InsertExternalIdentity_WhenUserAlreadyHasIdentityForProvider_ShouldViolateUniqueIndex()
    {
        // Arrange
        var userId = InsertUser(Faker.Internet.Email());
        InsertExternalIdentity(userId, ExternalProviderType.Google, Faker.Random.AlphaNumeric(21));

        // Act
        var insertSecondIdentity = () => InsertExternalIdentity(userId, ExternalProviderType.Google, Faker.Random.AlphaNumeric(21));

        // Assert
        insertSecondIdentity.Should().Throw<SqliteException>().WithMessage("*UNIQUE constraint failed: external_identities.user_id, external_identities.provider*");
    }

    [Fact]
    public void InsertExternalIdentity_WhenAnotherUserInTenantHasSameProviderUserId_ShouldViolateUniqueIndex()
    {
        // Arrange
        var providerUserId = Faker.Random.AlphaNumeric(21);
        var firstUserId = InsertUser(Faker.Internet.Email());
        var secondUserId = InsertUser(Faker.Internet.Email());
        InsertExternalIdentity(firstUserId, ExternalProviderType.Google, providerUserId);

        // Act
        var insertSecondIdentity = () => InsertExternalIdentity(secondUserId, ExternalProviderType.Google, providerUserId);

        // Assert
        insertSecondIdentity.Should().Throw<SqliteException>().WithMessage("*UNIQUE constraint failed: external_identities.provider, external_identities.provider_user_id, external_identities.tenant_id*");
    }
}
