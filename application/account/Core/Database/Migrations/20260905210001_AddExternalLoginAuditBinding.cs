using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Account.Database.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("20260905210001_AddExternalLoginAuditBinding")]
public sealed class AddExternalLoginAuditBinding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Later verification deployments may already have these audit columns.
        migrationBuilder.Sql("ALTER TABLE external_logins ADD COLUMN IF NOT EXISTS user_id text;");
        migrationBuilder.Sql("ALTER TABLE external_logins ADD COLUMN IF NOT EXISTS tenant_id bigint;");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_external_logins_user_id ON external_logins (user_id);");
    }
}
