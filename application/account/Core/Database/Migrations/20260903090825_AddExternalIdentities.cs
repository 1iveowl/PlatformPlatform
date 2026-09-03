using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Account.Database.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("20260903090825_AddExternalIdentities")]
public sealed class AddExternalIdentities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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
                assurance_level = table.Column<string>("text", nullable: true),
                verified_at = table.Column<DateTimeOffset>("timestamptz", nullable: true),
                issuer = table.Column<string>("text", nullable: false),
                subject = table.Column<string>("text", nullable: false),
                evidence_reference = table.Column<string>("text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_external_identities", x => x.id);
                table.ForeignKey("fk_external_identities_tenants_tenant_id", x => x.tenant_id, "tenants", "id");
                table.ForeignKey("fk_external_identities_users_user_id", x => x.user_id, "users", "id", onDelete: ReferentialAction.Cascade);
            }
        );

        migrationBuilder.CreateIndex("ix_external_identities_tenant_id", "external_identities", "tenant_id");
        migrationBuilder.CreateIndex("ix_external_identities_user_id", "external_identities", "user_id");
        migrationBuilder.CreateIndex("ix_external_identities_provider_provider_user_id_tenant_id", "external_identities", ["provider", "provider_user_id", "tenant_id"], unique: true);
    }
}
