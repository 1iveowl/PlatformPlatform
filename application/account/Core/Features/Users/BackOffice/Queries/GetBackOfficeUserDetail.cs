using Account.Features.Subscriptions.Domain;
using Account.Features.Tenants.BackOffice.Queries;
using Account.Features.Tenants.Domain;
using Account.Features.Users.Domain;
using JetBrains.Annotations;
using SharedKernel.Cqrs;
using SharedKernel.Domain;

namespace Account.Features.Users.BackOffice.Queries;

[PublicAPI]
public sealed record GetBackOfficeUserDetailQuery(UserId Id) : IRequest<Result<BackOfficeUserDetailResponse>>;

public sealed class GetBackOfficeUserDetailHandler(
    IUserRepository userRepository,
    ITenantRepository tenantRepository,
    ISubscriptionRepository subscriptionRepository
) : IRequestHandler<GetBackOfficeUserDetailQuery, Result<BackOfficeUserDetailResponse>>
{
    public async Task<Result<BackOfficeUserDetailResponse>> Handle(GetBackOfficeUserDetailQuery query, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdUnfilteredAsync(query.Id, cancellationToken);
        if (user is null)
        {
            return Result<BackOfficeUserDetailResponse>.NotFound($"User with id '{query.Id}' was not found.");
        }

        // The "Tenants" section on the User detail page lists every tenant this person belongs to. Each tenant has its
        // own user record (same email, different TenantId), so we look them up unfiltered by email. The lookup always
        // includes the queried user record itself, so its tenant is naturally part of the result.
        var membershipUsers = await userRepository.GetUsersByEmailUnfilteredAsync(user.Email, cancellationToken);
        var tenantIds = membershipUsers.Select(u => u.TenantId).Distinct().ToArray();
        var tenants = await tenantRepository.GetByIdsUnfilteredAsync(tenantIds, cancellationToken);
        var tenantsById = tenants.ToDictionary(t => t.Id);
        var subscriptions = await subscriptionRepository.GetByTenantIdsUnfilteredAsync(tenantIds, cancellationToken);
        var subscriptionsByTenantId = subscriptions.ToDictionary(s => s.TenantId);

        var memberships = membershipUsers.Select(u =>
            {
                var tenant = tenantsById.GetValueOrDefault(u.TenantId);
                var subscription = subscriptionsByTenantId.GetValueOrDefault(u.TenantId);
                var plannedChange = subscription switch
                {
                    { CancelAtPeriodEnd: true } => PlannedSubscriptionChange.Cancellation,
                    { ScheduledPlan: not null } => PlannedSubscriptionChange.ScheduledPlanChange,
                    _ => (PlannedSubscriptionChange?)null
                };
                var hasEverSubscribed = subscription?.PaymentTransactions
                    .Any(transaction => transaction.Status == PaymentTransactionStatus.Succeeded) == true;
                return new BackOfficeUserTenantMembership(
                    u.Id,
                    u.TenantId,
                    tenant?.Name ?? string.Empty,
                    tenant?.Logo.Url,
                    tenant?.Plan ?? SubscriptionPlan.Basis,
                    plannedChange,
                    hasEverSubscribed,
                    subscription?.CurrentPriceAmount,
                    subscription?.ScheduledPriceAmount,
                    subscription?.CurrentPriceCurrency,
                    subscription?.CurrentPeriodEnd,
                    subscription?.BillingInfo?.Address?.Country,
                    u.Role,
                    u.EmailConfirmed,
                    u.CreatedAt,
                    u.LastSeenAt
                );
            }
        ).ToArray();

        return new BackOfficeUserDetailResponse(
            user.Id,
            user.TenantId,
            tenantsById.GetValueOrDefault(user.TenantId)?.Name ?? string.Empty,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Title,
            user.Role,
            user.EmailConfirmed,
            user.Locale,
            user.CreatedAt,
            user.ModifiedAt,
            user.LastSeenAt,
            user.Avatar.Url,
            memberships,
            user.AbInclusionPin
        );
    }
}
