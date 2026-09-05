using SharedKernel.Database;

namespace Account.Database.DataMigrations;

/// <summary>Reconciles late legacy writes after old API and Worker revisions have been drained.</summary>
public sealed class ReconcileLegacyExternalIdentities(AccountDbContext accountDbContext, ILogger<BackfillExternalIdentities> logger) : IDataMigration
{
    public string Id => "20260905210002_ReconcileLegacyExternalIdentities";

    public TimeSpan Timeout => TimeSpan.FromMinutes(10);

    public Task<string> ExecuteAsync(CancellationToken cancellationToken)
    {
        return new BackfillExternalIdentities(accountDbContext, logger).ExecuteAsync(cancellationToken);
    }
}
