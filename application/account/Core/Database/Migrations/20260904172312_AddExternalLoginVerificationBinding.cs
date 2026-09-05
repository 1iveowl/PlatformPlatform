using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Account.Database.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("20260904172312_AddExternalLoginVerificationBinding")]
public sealed class AddExternalLoginVerificationBinding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Earlier identity branches already add the audit columns for login and signup.
        migrationBuilder.Sql("ALTER TABLE external_logins ADD COLUMN IF NOT EXISTS user_id text;");
        migrationBuilder.Sql("ALTER TABLE external_logins ADD COLUMN IF NOT EXISTS tenant_id bigint;");
        migrationBuilder.AddColumn<string>("session_id", "external_logins", "text", nullable: true);

        // The mock provider is selected per request from a cookie, independently at start and at callback. Recording
        // the choice lets the callback refuse a flow that started against the real provider, where the caller would
        // otherwise be able to choose the provider user id.
        migrationBuilder.AddColumn<bool>("used_mock_provider", "external_logins", "boolean", nullable: false, defaultValue: false);

        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_external_logins_user_id ON external_logins (user_id);");
    }
}
