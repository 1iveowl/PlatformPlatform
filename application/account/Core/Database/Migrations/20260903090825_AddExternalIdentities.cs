using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Account.Database.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("20260903090825_AddExternalIdentities")]
public sealed class AddExternalIdentities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The composite foreign key below needs a unique index over exactly the columns it references, so users
        // gains one first. Not CONCURRENTLY: the deployment generates the script with --idempotent, which wraps
        // every statement in a DO $EF$ ... END $EF$; block, and PostgreSQL refuses CREATE INDEX CONCURRENTLY
        // inside one. The SHARE lock the build takes blocks writes but not reads, and the foreign key below
        // locks users anyway.
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
                // Redundant for integrity once the composite key below validates tenant_id through users; kept
                // because every tenant-scoped table in this schema carries it, see sessions in the initial migration
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
