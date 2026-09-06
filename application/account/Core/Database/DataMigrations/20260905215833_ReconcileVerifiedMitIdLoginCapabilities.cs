using Microsoft.EntityFrameworkCore;
using SharedKernel.Database;

namespace Account.Database.DataMigrations;

/// <summary>Repairs verification-only rows after old verification writers have been drained.</summary>
public sealed class ReconcileVerifiedMitIdLoginCapabilities(AccountDbContext accountDbContext) : IDataMigration
{
    public string Id => "20260905215833_ReconcileVerifiedMitIdLoginCapabilities";

    public TimeSpan Timeout => TimeSpan.FromMinutes(10);

    public async Task<string> ExecuteAsync(CancellationToken cancellationToken)
    {
        var updated = await accountDbContext.Database.ExecuteSqlRawAsync(
            "UPDATE external_identities SET capabilities = 'Login, Verification' WHERE provider = 'MitId' AND capabilities = 'Verification';",
            cancellationToken
        );
        await accountDbContext.SaveChangesAsync(cancellationToken);
        return $"Granted Login to {updated} verified MitID identities";
    }
}
