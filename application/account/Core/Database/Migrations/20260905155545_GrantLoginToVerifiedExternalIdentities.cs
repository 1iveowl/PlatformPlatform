using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Account.Database.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("20260905155545_GrantLoginToVerifiedExternalIdentities")]
public sealed class GrantLoginToVerifiedExternalIdentities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Initial upgrade only. The data reconciliation covers old API callbacks completed during rollout.
        migrationBuilder.Sql("UPDATE external_identities SET capabilities = 'Login, Verification' WHERE provider = 'MitId' AND capabilities = 'Verification';");
    }
}
