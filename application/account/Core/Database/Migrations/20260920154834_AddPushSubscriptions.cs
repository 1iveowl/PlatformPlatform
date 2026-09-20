using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Account.Database.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("20260920154834_AddPushSubscriptions")]
public sealed class AddPushSubscriptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            "push_subscriptions",
            table => new
            {
                tenant_id = table.Column<long>("bigint", nullable: false),
                id = table.Column<string>("text", nullable: false),
                user_id = table.Column<string>("text", nullable: false),
                created_at = table.Column<DateTimeOffset>("timestamptz", nullable: false),
                modified_at = table.Column<DateTimeOffset>("timestamptz", nullable: true),
                endpoint = table.Column<string>("text", nullable: false),
                public_key = table.Column<string>("text", nullable: false),
                auth_secret = table.Column<string>("text", nullable: false),
                device_label = table.Column<string>("text", nullable: false),
                application_path = table.Column<string>("text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_push_subscriptions", x => x.id);
                table.ForeignKey("fk_push_subscriptions_tenants_tenant_id", x => x.tenant_id, "tenants", "id");
                table.ForeignKey("fk_push_subscriptions_users_user_id", x => x.user_id, "users", "id", onDelete: ReferentialAction.Cascade);
            }
        );

        migrationBuilder.CreateIndex("ix_push_subscriptions_tenant_id", "push_subscriptions", "tenant_id");
        // One row per browser subscription: the push service address is what identifies it, and the same user
        // resubscribing in the same browser updates that row rather than adding another
        migrationBuilder.CreateIndex("ix_push_subscriptions_user_id_endpoint", "push_subscriptions", ["user_id", "endpoint"], unique: true);
    }
}
