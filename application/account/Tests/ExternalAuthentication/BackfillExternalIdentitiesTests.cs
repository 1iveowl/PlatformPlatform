using Account.Database.DataMigrations;
using Account.Features.ExternalAuthentication.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Domain;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

public sealed class BackfillExternalIdentitiesTests : ExternalAuthenticationTestBase
{
    [Fact]
    public async Task ExecuteAsync_WhenRunTwice_ShouldInsertOneRowPerJsonIdentityWithoutDuplicates()
    {
        // Arrange
        var firstProviderUserId = Faker.Random.AlphaNumeric(21);
        var secondProviderUserId = Faker.Random.AlphaNumeric(21);
        var migratedProviderUserId = Faker.Random.AlphaNumeric(21);
        var otherTenantProviderUserId = Faker.Random.AlphaNumeric(21);
        var deletedProviderUserId = Faker.Random.AlphaNumeric(21);
        var otherTenantId = InsertTenant();
        var firstUserId = InsertUserWithExternalIdentity(Faker.Internet.Email(), ExternalProviderType.Google, firstProviderUserId);
        var secondUserId = InsertUserWithExternalIdentity(Faker.Internet.Email(), ExternalProviderType.Google, secondProviderUserId);
        var migratedUserId = InsertUserWithExternalIdentity(Faker.Internet.Email(), ExternalProviderType.Google, migratedProviderUserId);
        var migratedExternalIdentityId = InsertExternalIdentity(migratedUserId, ExternalProviderType.Google, migratedProviderUserId);
        var otherTenantUserId = InsertUserWithExternalIdentity(Faker.Internet.Email(), ExternalProviderType.Google, otherTenantProviderUserId, otherTenantId);
        var deletedUserId = InsertUserWithExternalIdentity(Faker.Internet.Email(), ExternalProviderType.Google, deletedProviderUserId);
        Connection.Update("users", "id", deletedUserId.ToString(), [("deleted_at", TimeProvider.GetUtcNow())]);

        // Act
        var firstSummary = await RunBackfillExternalIdentities();
        var secondSummary = await RunBackfillExternalIdentities();

        // Assert
        firstSummary.Should().Be("Inserted 4 external identities, skipped 1 that already had a row, gave 0 to a live user over a soft-deleted one and skipped 0 contested between live users in the same tenant");
        secondSummary.Should().Be("Inserted 0 external identities, skipped 5 that already had a row, gave 0 to a live user over a soft-deleted one and skipped 0 contested between live users in the same tenant");
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_identities", []).Should().Be(5);
        AssertLoginIdentity(firstUserId, DatabaseSeeder.Tenant1.Id, firstProviderUserId, "https://accounts.google.com");
        AssertLoginIdentity(secondUserId, DatabaseSeeder.Tenant1.Id, secondProviderUserId, "https://accounts.google.com");
        AssertLoginIdentity(migratedUserId, DatabaseSeeder.Tenant1.Id, migratedProviderUserId, "https://mock.localhost/google");
        AssertLoginIdentity(otherTenantUserId, otherTenantId, otherTenantProviderUserId, "https://accounts.google.com");
        AssertLoginIdentity(deletedUserId, DatabaseSeeder.Tenant1.Id, deletedProviderUserId, "https://accounts.google.com");
        Connection.ExecuteScalar<string>("SELECT id FROM external_identities WHERE user_id = @user_id", [new { user_id = migratedUserId.ToString() }])
            .Should().Be(migratedExternalIdentityId.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_WhenLiveAndDeletedUserShareIdentityInTenant_ShouldInsertRowForLiveUserOnly()
    {
        // Arrange
        var providerUserId = Faker.Random.AlphaNumeric(21);
        var deletedUserId = InsertUserWithExternalIdentity(Faker.Internet.Email(), ExternalProviderType.Google, providerUserId);
        Connection.Update("users", "id", deletedUserId.ToString(), [("deleted_at", TimeProvider.GetUtcNow())]);
        var liveUserId = InsertUserWithExternalIdentity(Faker.Internet.Email(), ExternalProviderType.Google, providerUserId);

        // Act
        var summary = await RunBackfillExternalIdentities();

        // Assert
        summary.Should().Be("Inserted 1 external identities, skipped 0 that already had a row, gave 1 to a live user over a soft-deleted one and skipped 0 contested between live users in the same tenant");
        object[] parameters = [new { provider_user_id = providerUserId }];
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_identities WHERE provider_user_id = @provider_user_id", parameters).Should().Be(1);
        Connection.ExecuteScalar<string>("SELECT user_id FROM external_identities WHERE provider_user_id = @provider_user_id", parameters).Should().Be(liveUserId.ToString());
    }

    // The next two tests are the only proof of the contested-key policy. A real-data backfill cannot exercise it,
    // because no local database holds a key shared by two live users and manufacturing one means writing jsonb into
    // the users table, so do not remove them without replacing the coverage.
    [Fact]
    public async Task ExecuteAsync_WhenTwoLiveUsersShareIdentityInTenant_ShouldInsertNoRow()
    {
        // Arrange
        var providerUserId = Faker.Random.AlphaNumeric(21);
        InsertUserWithExternalIdentity(Faker.Internet.Email(), ExternalProviderType.Google, providerUserId);
        InsertUserWithExternalIdentity(Faker.Internet.Email(), ExternalProviderType.Google, providerUserId);

        // Act
        var summary = await RunBackfillExternalIdentities();

        // Assert
        summary.Should().Be("Inserted 0 external identities, skipped 0 that already had a row, gave 0 to a live user over a soft-deleted one and skipped 1 contested between live users in the same tenant");
        Connection.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM external_identities WHERE provider_user_id = @provider_user_id", [new { provider_user_id = providerUserId }]
        ).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTwoLiveUsersAndDeletedUserShareIdentityInTenant_ShouldInsertNoRow()
    {
        // Arrange
        var providerUserId = Faker.Random.AlphaNumeric(21);
        var deletedUserId = InsertUserWithExternalIdentity(Faker.Internet.Email(), ExternalProviderType.Google, providerUserId);
        Connection.Update("users", "id", deletedUserId.ToString(), [("deleted_at", TimeProvider.GetUtcNow())]);
        InsertUserWithExternalIdentity(Faker.Internet.Email(), ExternalProviderType.Google, providerUserId);
        InsertUserWithExternalIdentity(Faker.Internet.Email(), ExternalProviderType.Google, providerUserId);

        // Act
        var summary = await RunBackfillExternalIdentities();

        // Assert
        summary.Should().Be("Inserted 0 external identities, skipped 0 that already had a row, gave 0 to a live user over a soft-deleted one and skipped 1 contested between live users in the same tenant");
        Connection.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM external_identities WHERE provider_user_id = @provider_user_id", [new { provider_user_id = providerUserId }]
        ).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoUserHoldsALegacyIdentity_ShouldInsertNothing()
    {
        // Arrange
        InsertUser(Faker.Internet.Email());

        // Act
        var summary = await RunBackfillExternalIdentities();

        // Assert
        summary.Should().Be("Inserted 0 external identities, skipped 0 that already had a row, gave 0 to a live user over a soft-deleted one and skipped 0 contested between live users in the same tenant");
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_identities", []).Should().Be(0);
    }

    // A login on the new API between the schema migration and the backfill links the user under the provider user id
    // the token carried. When the account changed at the provider that is a different key from the jsonb entry, and
    // the (user_id, provider) unique index would abort the whole migration on the insert.
    [Fact]
    public async Task ExecuteAsync_WhenHolderAlreadyHasRowForProviderUnderAnotherKey_ShouldSkipLegacyEntry()
    {
        // Arrange
        var legacyProviderUserId = Faker.Random.AlphaNumeric(21);
        var currentProviderUserId = Faker.Random.AlphaNumeric(21);
        var userId = InsertUserWithExternalIdentity(Faker.Internet.Email(), ExternalProviderType.Google, legacyProviderUserId);
        InsertExternalIdentity(userId, ExternalProviderType.Google, currentProviderUserId);

        // Act
        var summary = await RunBackfillExternalIdentities();

        // Assert
        summary.Should().Be("Inserted 0 external identities, skipped 1 that already had a row, gave 0 to a live user over a soft-deleted one and skipped 0 contested between live users in the same tenant");
        object[] parameters = [new { user_id = userId.ToString() }];
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_identities WHERE user_id = @user_id", parameters).Should().Be(1);
        Connection.ExecuteScalar<string>("SELECT provider_user_id FROM external_identities WHERE user_id = @user_id", parameters).Should().Be(currentProviderUserId);
    }

    private async Task<string> RunBackfillExternalIdentities()
    {
        using var scope = WebApplicationServices.CreateScope();
        var dataMigration = ActivatorUtilities.CreateInstance<BackfillExternalIdentities>(scope.ServiceProvider);
        return await dataMigration.ExecuteAsync(CancellationToken.None);
    }

    private void AssertLoginIdentity(UserId userId, TenantId tenantId, string providerUserId, string issuer)
    {
        object[] parameters = [new { user_id = userId.ToString() }];
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM external_identities WHERE user_id = @user_id", parameters).Should().Be(1);
        Connection.ExecuteScalar<long>("SELECT tenant_id FROM external_identities WHERE user_id = @user_id", parameters).Should().Be(tenantId.Value);
        Connection.ExecuteScalar<string>("SELECT provider FROM external_identities WHERE user_id = @user_id", parameters).Should().Be(nameof(ExternalProviderType.Google));
        Connection.ExecuteScalar<string>("SELECT provider_user_id FROM external_identities WHERE user_id = @user_id", parameters).Should().Be(providerUserId);
        Connection.ExecuteScalar<string>("SELECT capabilities FROM external_identities WHERE user_id = @user_id", parameters).Should().Be(nameof(ExternalIdentityCapabilities.Login));
        Connection.ExecuteScalar<string>("SELECT issuer FROM external_identities WHERE user_id = @user_id", parameters).Should().Be(issuer);
        Connection.ExecuteScalar<string>("SELECT subject FROM external_identities WHERE user_id = @user_id", parameters).Should().Be(providerUserId);
    }
}
