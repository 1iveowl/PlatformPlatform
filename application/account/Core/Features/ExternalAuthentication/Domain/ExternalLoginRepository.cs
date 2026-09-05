using Account.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain;
using SharedKernel.Persistence;

namespace Account.Features.ExternalAuthentication.Domain;

public interface IExternalLoginRepository : IAppendRepository<ExternalLogin, ExternalLoginId>
{
    void Update(ExternalLogin aggregate);

    /// <summary>Returns attempts bound to this user and unbound legacy attempts matching their email.</summary>
    Task<ExternalLogin[]> GetByUserSinceAsync(UserId userId, string email, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>
    ///     Returns every successful external login created at or after <paramref name="since" />. Used by the back-office
    ///     dashboard to aggregate successful external login activity per day across all tenants.
    /// </summary>
    Task<ExternalLogin[]> GetSucceededSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

public sealed class ExternalLoginRepository(AccountDbContext accountDbContext)
    : RepositoryBase<ExternalLogin, ExternalLoginId>(accountDbContext), IExternalLoginRepository
{
    public async Task<ExternalLogin[]> GetByUserSinceAsync(UserId userId, string email, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var logins = await DbSet.Where(el => el.UserId == userId || (el.UserId == null && el.Email == email.ToLowerInvariant()))
            .ToArrayAsync(cancellationToken);
        return logins.Where(el => el.CreatedAt >= since).ToArray();
    }

    /// <summary>
    ///     Returns every successful external login created at or after <paramref name="since" />. Used by the back-office
    ///     dashboard to aggregate successful external login activity per day across all tenants. SQLite cannot translate
    ///     DateTimeOffset comparisons, so the time filter runs in memory; the dashboard period is bounded (max 90 days)
    ///     so the materialized set stays small.
    /// </summary>
    public async Task<ExternalLogin[]> GetSucceededSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var logins = await DbSet.Where(el => el.LoginResult == ExternalLoginResult.Success).ToArrayAsync(cancellationToken);
        return logins.Where(el => el.CreatedAt >= since).ToArray();
    }
}
