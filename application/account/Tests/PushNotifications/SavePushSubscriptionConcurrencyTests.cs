using Account.Database;
using Account.Features.PushNotifications.Domain;
using Account.Features.PushNotifications.Shared;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel.ExecutionContext;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.PushNotifications;

/// <summary>
///     The limit on devices per user under two saves of the same user that overlap. Counting the rows and then inserting
///     cannot hold the limit, because the count is already stale when the row is written; the unique index on
///     (user_id, device_slot) can, because two saves at the limit have only one free slot to choose and the database
///     admits one of them. The race is driven here rather than described: each save runs in its own database session and
///     both read before either writes, which is the interleaving a count guard cannot survive.
/// </summary>
public sealed class SavePushSubscriptionConcurrencyTests(PushNotificationsWebApplicationFactory factory)
    : EndpointBaseTest<AccountDbContext>(factory), IClassFixture<PushNotificationsWebApplicationFactory>
{
    [Fact]
    public async Task SavePushSubscription_WhenTwoSavesOfTheSameUserRaceAtTheLimit_ShouldAdmitOnlyOne()
    {
        // Arrange
        InsertSubscriptionsForOwner(PushNotificationPolicy.MaximumSubscriptionsPerUser - 1);
        await using var sessionA = OpenSession();
        await using var sessionB = OpenSession();
        await using var contextA = CreateContext(sessionA);
        await using var contextB = CreateContext(sessionB);
        var repositoryA = new PushSubscriptionRepository(contextA);
        var repositoryB = new PushSubscriptionRepository(contextB);

        // Act
        var slotA = PushNotificationPolicy.FindFreeDeviceSlot(await repositoryA.GetUsedDeviceSlotsAsync(DatabaseSeeder.Tenant1Owner.Id, CancellationToken.None));
        var slotB = PushNotificationPolicy.FindFreeDeviceSlot(await repositoryB.GetUsedDeviceSlotsAsync(DatabaseSeeder.Tenant1Owner.Id, CancellationToken.None));
        await repositoryA.AddAsync(CreateSubscription("race-a", slotA!.Value), CancellationToken.None);
        await contextA.SaveChangesAsync(CancellationToken.None);
        await repositoryB.AddAsync(CreateSubscription("race-b", slotB!.Value), CancellationToken.None);
        DbUpdateException? refusal = null;
        try
        {
            await contextB.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException exception)
        {
            refusal = exception;
        }

        // Assert
        slotB.Should().Be(slotA, "both saves read the same state, which is what a count guard would have let past the limit");
        refusal.Should().NotBeNull("the unique index on (user_id, device_slot) is what refuses the second save");
        CountSubscriptions().Should().Be(PushNotificationPolicy.MaximumSubscriptionsPerUser);
    }

    [Fact]
    public async Task SavePushSubscription_WhenTwoSavesOfTheSameUserRaceBelowTheLimit_ShouldAdmitOnlyOnePerSlot()
    {
        // Arrange
        InsertSubscriptionsForOwner(1);
        await using var sessionA = OpenSession();
        await using var sessionB = OpenSession();
        await using var contextA = CreateContext(sessionA);
        await using var contextB = CreateContext(sessionB);
        var repositoryA = new PushSubscriptionRepository(contextA);
        var repositoryB = new PushSubscriptionRepository(contextB);

        // Act: the loser is refused the slot it read, and the device it stands for is saved by asking again
        var slotA = PushNotificationPolicy.FindFreeDeviceSlot(await repositoryA.GetUsedDeviceSlotsAsync(DatabaseSeeder.Tenant1Owner.Id, CancellationToken.None));
        await repositoryA.AddAsync(CreateSubscription("below-a", slotA!.Value), CancellationToken.None);
        await contextA.SaveChangesAsync(CancellationToken.None);
        var slotAfterTheFirstSave = PushNotificationPolicy.FindFreeDeviceSlot(await repositoryB.GetUsedDeviceSlotsAsync(DatabaseSeeder.Tenant1Owner.Id, CancellationToken.None));
        await repositoryB.AddAsync(CreateSubscription("below-b", slotAfterTheFirstSave!.Value), CancellationToken.None);
        await contextB.SaveChangesAsync(CancellationToken.None);

        // Assert
        slotA.Should().Be(1);
        slotAfterTheFirstSave.Should().Be(2);
        CountSubscriptions().Should().Be(3);
    }

    private void InsertSubscriptionsForOwner(int subscriptionCount)
    {
        for (var deviceSlot = 0; deviceSlot < subscriptionCount; deviceSlot++)
        {
            Connection.Insert("push_subscriptions", [
                    ("tenant_id", DatabaseSeeder.Tenant1.Id.Value),
                    ("id", PushSubscriptionId.NewId().Value),
                    ("user_id", DatabaseSeeder.Tenant1Owner.Id.Value),
                    ("device_slot", deviceSlot),
                    ("created_at", TimeProvider.GetUtcNow()),
                    ("modified_at", null),
                    ("endpoint", $"https://fcm.googleapis.com/fcm/send/race-seed-{deviceSlot}"),
                    ("public_key", PushNotificationsWebApplicationFactory.SubscriptionPublicKey),
                    ("auth_secret", PushNotificationsWebApplicationFactory.SubscriptionAuthSecret),
                    ("device_label", "Chrome on Linux"),
                    ("application_path", "/blazor/app")
                ]
            );
        }
    }

    private long CountSubscriptions()
    {
        return Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM push_subscriptions", []);
    }

    private PushSubscription CreateSubscription(string name, int deviceSlot)
    {
        return PushSubscription.Create(
            DatabaseSeeder.Tenant1.Id, DatabaseSeeder.Tenant1Owner.Id, deviceSlot, $"https://fcm.googleapis.com/fcm/send/{name}",
            PushNotificationsWebApplicationFactory.SubscriptionPublicKey, PushNotificationsWebApplicationFactory.SubscriptionAuthSecret, "Chrome on Linux", "/blazor/app"
        );
    }

    // A second connection to the same shared-cache in-memory database, which is a database session of its own and is what
    // makes the two saves overlap rather than follow each other
    private SqliteConnection OpenSession()
    {
        var session = new SqliteConnection(Connection.ConnectionString);
        session.Open();
        return session;
    }

    private AccountDbContext CreateContext(SqliteConnection session)
    {
        var executionContext = Substitute.For<IExecutionContext>();
        executionContext.TenantId.Returns(DatabaseSeeder.Tenant1.Id);
        var options = new DbContextOptionsBuilder<AccountDbContext>().UseSqlite(session).UseSnakeCaseNamingConvention().Options;
        return new AccountDbContext(options, executionContext, TimeProvider);
    }
}
