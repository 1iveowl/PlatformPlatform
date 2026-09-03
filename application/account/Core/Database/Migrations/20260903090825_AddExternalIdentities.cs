using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Account.Database.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("20260903090825_AddExternalIdentities")]
public sealed class AddExternalIdentities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The composite foreign key below needs a unique constraint or a non-partial unique index over exactly the
        // columns it references, so users gains one first. It restricts no existing data, because id is already the
        // primary key and therefore globally unique, which makes every (tenant_id, id) pair unique too. It is
        // created without CONCURRENTLY although users is a large table: the deployment generates the script with
        // `dotnet ef migrations script --idempotent`, which wraps every statement, transaction-suppressed ones
        // included, in a `DO $EF$ ... END $EF$;` block, and PostgreSQL refuses CREATE INDEX CONCURRENTLY inside
        // one. The build takes a SHARE lock on users, blocking writes but not reads, and the foreign key below
        // needs a lock on users in any case.
        migrationBuilder.CreateIndex("ix_users_tenant_id_id", "users", ["tenant_id", "id"], unique: true);

        migrationBuilder.CreateTable(
            "external_identities",
            table => new
            {
                tenant_id = table.Column<long>("bigint", nullable: false),
                id = table.Column<string>("text", nullable: false),
                user_id = table.Column<string>("text", nullable: false),
                created_at = table.Column<DateTimeOffset>("timestamptz", nullable: false),
                modified_at = table.Column<DateTimeOffset>("timestamptz", nullable: true),
                provider = table.Column<string>("text", nullable: false),
                provider_user_id = table.Column<string>("text", nullable: false),
                capabilities = table.Column<string>("text", nullable: false),
                issuer = table.Column<string>("text", nullable: false),
                subject = table.Column<string>("text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_external_identities", x => x.id);
                // Redundant for integrity once the composite key below is in place: it validates tenant_id against
                // users, and fk_users_tenants_tenant_id validates that against tenants. It is kept because every
                // tenant-scoped table in this schema carries this foreign key, and dropping it here alone would be
                // an unexplained deviation; the cost is one index probe per insert on a table written about once
                // per signup or identity link.
                table.ForeignKey("fk_external_identities_tenants_tenant_id", x => x.tenant_id, "tenants", "id");
                // Composite, so a row's tenant must be the tenant of its own user. See ExternalIdentityConfiguration
                table.ForeignKey(
                    "fk_external_identities_users_tenant_id_user_id", x => new { x.tenant_id, x.user_id }, "users", ["tenant_id", "id"], onDelete: ReferentialAction.Cascade
                );
            }
        );

        migrationBuilder.CreateIndex("ix_external_identities_tenant_id", "external_identities", "tenant_id");
        migrationBuilder.CreateIndex("ix_external_identities_user_id_provider", "external_identities", ["user_id", "provider"], unique: true);
        migrationBuilder.CreateIndex("ix_external_identities_provider_provider_user_id_tenant_id", "external_identities", ["provider", "provider_user_id", "tenant_id"], unique: true);
    }
}
