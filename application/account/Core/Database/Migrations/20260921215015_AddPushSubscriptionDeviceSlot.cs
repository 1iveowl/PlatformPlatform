using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Account.Database.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("20260921215015_AddPushSubscriptionDeviceSlot")]
public sealed class AddPushSubscriptionDeviceSlot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("device_slot", "push_subscriptions", "integer", nullable: false, defaultValue: 0);

        // Existing rows are numbered per user in the order they were made, so the unique index below can be created. A
        // user who is already over the limit keeps every row and is simply refused the next device until they drop back
        // under it.
        migrationBuilder.Sql(
            """
            UPDATE push_subscriptions
            SET device_slot = numbered.device_slot
            FROM (SELECT id, row_number() OVER (PARTITION BY user_id ORDER BY created_at, id) - 1 AS device_slot FROM push_subscriptions) AS numbered
            WHERE push_subscriptions.id = numbered.id;
            """
        );

        // The cap on devices per user: the save handler picks a slot the user is not holding, and two saves racing at the
        // limit pick the same one, so this index is what keeps a burst from inserting past the limit
        migrationBuilder.CreateIndex("ix_push_subscriptions_user_id_device_slot", "push_subscriptions", ["user_id", "device_slot"], unique: true);
    }
}
