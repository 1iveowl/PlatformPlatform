using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Account.Database.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("20260904172312_AddExternalLoginVerificationBinding")]
public sealed class AddExternalLoginVerificationBinding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The verification flow runs from an authenticated session and binds a provider identity to the user who
        // started it, so the flow row records who that was. Login and signup leave all four values unset.
        migrationBuilder.AddColumn<string>("user_id", "external_logins", "text", nullable: true);
        migrationBuilder.AddColumn<long>("tenant_id", "external_logins", "bigint", nullable: true);
        migrationBuilder.AddColumn<string>("session_id", "external_logins", "text", nullable: true);

        // The mock provider is selected per request from a cookie, independently at start and at callback. Recording
        // the choice lets the callback refuse a flow that started against the real provider, where the caller would
        // otherwise be able to choose the provider user id.
        migrationBuilder.AddColumn<bool>("used_mock_provider", "external_logins", "boolean", nullable: false, defaultValue: false);

        migrationBuilder.CreateIndex("ix_external_logins_user_id", "external_logins", "user_id");
    }
}
