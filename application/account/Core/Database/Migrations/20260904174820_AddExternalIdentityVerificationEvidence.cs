using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Account.Database.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("20260904174820_AddExternalIdentityVerificationEvidence")]
public sealed class AddExternalIdentityVerificationEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Evidence of the most recent successful identity verification. Null on every existing row and on every row
        // created by a login or a signup, because only the verification callback writes these.
        migrationBuilder.AddColumn<string>("assurance_level", "external_identities", "text", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("verified_at", "external_identities", "timestamptz", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("authenticated_at", "external_identities", "timestamptz", nullable: true);
        migrationBuilder.AddColumn<string>("verified_by_external_login_id", "external_identities", "text", nullable: true);
    }
}
