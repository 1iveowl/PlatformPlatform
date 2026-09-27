using Account.Features.Subscriptions.Domain;
using Account.Features.Tenants.Domain;
using Account.Integrations.Stripe;
using JetBrains.Annotations;
using SharedKernel.Cqrs;
using SharedKernel.Domain;

namespace Account.Features.Tenants.BackOffice.Queries;

[PublicAPI]
public sealed record GetTenantDetailQuery(TenantId Id) : IRequest<Result<TenantDetailResponse>>;

public sealed class GetTenantDetailHandler(ITenantRepository tenantRepository, ISubscriptionRepository subscriptionRepository, StripeClientFactory stripeClientFactory)
    : IRequestHandler<GetTenantDetailQuery, Result<TenantDetailResponse>>
{
    public async Task<Result<TenantDetailResponse>> Handle(GetTenantDetailQuery query, CancellationToken cancellationToken)
    {
        var tenant = await tenantRepository.GetByIdUnfilteredAsync(query.Id, cancellationToken);
        if (tenant is null)
        {
            return Result<TenantDetailResponse>.NotFound($"Tenant with id '{query.Id}' was not found.");
        }

        var subscription = await subscriptionRepository.GetByTenantIdUnfilteredAsync(tenant.Id, cancellationToken);

        // Net lifetime value: only count successful payments that were NOT later credit-noted or refunded.
        // Money returned to the customer (via credit note or refund) is not revenue, so it doesn't contribute.
        var lifetimeValue = subscription?.PaymentTransactions
            .Where(t => t is { Status: PaymentTransactionStatus.Succeeded, CreditNoteUrl: null, RefundedAt: null })
            .Sum(t => t.AmountExcludingTax);

        var hasEverSubscribed = subscription?.PaymentTransactions
            .Any(t => t.Status is PaymentTransactionStatus.Succeeded or PaymentTransactionStatus.Refunded) == true;

        var billingAddress = subscription?.BillingInfo?.Address is { } address
            ? new BillingAddressResponse(address.Line1, address.Line2, address.PostalCode, address.City, address.State, address.Country)
            : null;

        var paymentMethod = subscription?.PaymentMethod is { } currentPaymentMethod
            ? new PaymentMethodResponse(currentPaymentMethod.Brand, currentPaymentMethod.Last4, currentPaymentMethod.ExpMonth, currentPaymentMethod.ExpYear)
            : null;

        return new TenantDetailResponse(
            tenant.Id,
            tenant.Name,
            tenant.Plan,
            subscription?.ScheduledPlan,
            subscription?.ScheduledPriceAmount,
            subscription?.CancelAtPeriodEnd ?? false,
            subscription?.CurrentPriceAmount,
            subscription?.CurrentPriceCurrency,
            subscription?.CurrentPeriodEnd,
            subscription?.SubscribedSince,
            hasEverSubscribed,
            subscription?.BillingInfo?.Name,
            subscription?.BillingInfo?.TaxId,
            billingAddress,
            paymentMethod,
            lifetimeValue,
            tenant.State,
            tenant.SuspensionReason,
            tenant.SuspendedAt,
            tenant.Logo.Url,
            tenant.CreatedAt,
            tenant.ModifiedAt,
            subscription?.HasDriftDetected ?? false,
            subscription?.DriftCheckedAt,
            subscription?.DriftDiscrepancies.ToArray() ?? [],
            subscription?.StripeCustomerId is { } stripeCustomerId ? stripeClientFactory.GetClient().BuildCustomerDashboardUrl(stripeCustomerId) : null,
            tenant.AbInclusionPin
        );
    }
}
