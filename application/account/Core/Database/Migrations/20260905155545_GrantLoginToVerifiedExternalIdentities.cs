using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Account.Database.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("20260905155545_GrantLoginToVerifiedExternalIdentities")]
public sealed class GrantLoginToVerifiedExternalIdentities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // A verified MitID identity now carries the right to log in, granted by the verification itself. Rows written
        // before that are just as verified, so they get the same right rather than requiring the person to verify
        // again. Leaving them behind would create two classes of identical-looking rows whose behaviour differed only
        // by when they were written.
        // Capabilities are stored as the flag names joined by a comma and a space, so the match is on the exact
        // string a Verification-only row holds. Rows already carrying Login are left alone, which makes this safe to
        // run again.
        migrationBuilder.Sql("UPDATE external_identities SET capabilities = 'Login, Verification' WHERE provider = 'MitId' AND capabilities = 'Verification';");
    }
}
