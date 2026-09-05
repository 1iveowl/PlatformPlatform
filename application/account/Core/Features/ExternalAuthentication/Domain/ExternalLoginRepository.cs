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
    ///     Returns every successful login or signup created at or after <paramref name="since" />. Identity
    ///     verifications are excluded: they succeed the same way but sign nobody in, so counting them as logins would
    ///     overstate sign-in activity.
    /// </summary>
    Task<ExternalLogin[]> GetSucceededSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

public sealed class ExternalLoginRepository(AccountDbContext accountDbContext)
    : RepositoryBase<ExternalLogin, ExternalLoginId>(accountDbContext), IExternalLoginRepository
{
    public async Task<ExternalLogin[]> GetByUserSinceAsync(UserId userId, string email, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var logins = await DbSet.Where(el => (el.Type == ExternalLoginType.Login || el.Type == ExternalLoginType.Signup)
                && (el.UserId == userId || (el.UserId == null && el.Email == email.ToLowerInvariant())))
            .ToArrayAsync(cancellationToken);
        return logins.Where(el => el.CreatedAt >= since).ToArray();
    }

    /// <summary>
    ///     Returns every successful login or signup created at or after <paramref name="since" />, excluding identity
    ///     verifications. Used by the back-office dashboard to aggregate sign-in activity per day across all tenants.
    ///     SQLite cannot translate DateTimeOffset comparisons, so the time filter runs in memory; the dashboard period
    ///     is bounded (max 90 days) so the materialized set stays small.
    /// </summary>
    public async Task<ExternalLogin[]> GetSucceededSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        // A successful identity verification is also a completed external login row, but it signs nobody in, so the
        // flow type is filtered here rather than by every caller counting logins
        var logins = await DbSet
            .Where(el => el.LoginResult == ExternalLoginResult.Success && (el.Type == ExternalLoginType.Login || el.Type == ExternalLoginType.Signup))
            .ToArrayAsync(cancellationToken);
        return logins.Where(el => el.CreatedAt >= since).ToArray();
    }
}
