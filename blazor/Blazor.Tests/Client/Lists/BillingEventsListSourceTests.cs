using Account.Client;
using Account.Features.BackOffice.BillingEvents.Queries;
using Account.Features.Subscriptions.Domain;
using Blazor.Client.BackOffice.Accounts;
using Blazor.Client.BackOffice.Billing;
using Blazor.Client.Components.Lists;
using FluentAssertions;
using SharedKernel.Domain;

namespace Blazor.Tests.Client.Lists;

// The back office's billing events list and the account's Billing events tab: the view is the React back office's view
// parameter mapped to billingEventCategories.ts, the URL keeps the React names and writes back what it read, and the account's
// tab asks for that account's events only.
public sealed class BillingEventsListSourceTests
{
    private const string BillingEventsUrl = "https://back-office.dev.localhost:9001/blazor/back-office/billing-events";

    [Theory]
    [InlineData(null, BillingEventsView.All)]
    [InlineData("all", BillingEventsView.All)]
    [InlineData("mrr", BillingEventsView.Mrr)]
    [InlineData("state", BillingEventsView.State)]
    [InlineData("other", BillingEventsView.Other)]
    [InlineData("MRR", BillingEventsView.All)]
    [InlineData("payments", BillingEventsView.All)]
    public void GetView_ShouldAcceptTheReactValuesOnlyWithAllAsTheFallback(string? value, BillingEventsView expected)
    {
        // Arrange
        var filters = value is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [BillingEventsListSource.ViewParameter] = value };

        // Act
        var view = BillingEventsListSource.GetView(filters);

        // Assert
        view.Should().Be(expected);
    }

    [Fact]
    public void GetEventTypes_ShouldMapEachViewToTheReactCategoryAndLeaveNoTypeOutsideAView()
    {
        // Act
        var all = BillingEventsListSource.GetEventTypes(BillingEventsView.All);
        var mrr = BillingEventsListSource.GetEventTypes(BillingEventsView.Mrr);
        var state = BillingEventsListSource.GetEventTypes(BillingEventsView.State);
        var other = BillingEventsListSource.GetEventTypes(BillingEventsView.Other);

        // Assert
        all.Should().BeEmpty();
        mrr.Should().HaveCount(9).And.Contain(BillingEventType.SubscriptionDowngradeScheduled).And.NotContain(BillingEventType.SubscriptionRenewed);
        state.Should().Equal(
            BillingEventType.SubscriptionCreated, BillingEventType.SubscriptionUpgraded, BillingEventType.SubscriptionDowngraded, BillingEventType.SubscriptionExpired,
            BillingEventType.SubscriptionImmediatelyCancelled, BillingEventType.SubscriptionSuspended
        );
        other.Should().HaveCount(10).And.Contain(BillingEventType.SubscriptionRenewed).And.Contain(BillingEventType.Unclassified);
        mrr.Concat(state).Concat(other).Distinct().Should().BeEquivalentTo(Enum.GetValues<BillingEventType>());
    }

    [Fact]
    public void Parse_WhenTheUrlComesFromTheReactBackOffice_ShouldReadTheSameStateAndWriteTheSameUrl()
    {
        // Arrange: a URL as the React router writes it, the MRR view sorted by account in its default (descending) order
        var url = $"{BillingEventsUrl}?search=acme&view=mrr&orderBy=TenantName&pageOffset=2";

        // Act
        var state = DataListState.Parse(url, CreateOptions());
        var query = BillingEventsListSource.ToQuery(new DataListRequest(state.Filters, state.OrderBy, state.SortOrder, state.PageOffset, 25));

        // Assert
        state.ToUri(url, CreateOptions()).Should().Be(url);
        query.EventTypes.Should().Equal(BillingEventsListSource.MrrImpactEventTypes);
        AccountApiRoutes.BackOfficeBillingEvents(query).Should().StartWith("/api/back-office/billing-events?Search=acme&EventTypes=SubscriptionCreated&EventTypes=SubscriptionUpgraded&")
            .And.EndWith("&OrderBy=TenantName&SortOrder=Descending&PageOffset=2&PageSize=25");
    }

    [Fact]
    public void Parse_WhenTheUrlHasNoSortOrView_ShouldUseTheServerDefaultAndWriteNothing()
    {
        // Act
        var state = DataListState.Parse($"{BillingEventsUrl}?view=all", CreateOptions());
        var query = BillingEventsListSource.ToQuery(new DataListRequest(state.Filters, state.OrderBy, state.SortOrder, state.PageOffset, 25));

        // Assert
        state.ToUri(BillingEventsUrl, CreateOptions()).Should().Be(BillingEventsUrl);
        AccountApiRoutes.BackOfficeBillingEvents(query).Should().Be("/api/back-office/billing-events?OrderBy=OccurredAt&SortOrder=Descending&PageSize=25");
    }

    [Fact]
    public void SetView_ShouldLeaveAllOutOfTheUrl()
    {
        // Act
        var all = BillingEventsListSource.SetView(BillingEventsView.All);
        var other = BillingEventsListSource.SetView(BillingEventsView.Other);

        // Assert
        all.Should().BeEquivalentTo(new Dictionary<string, string?> { ["view"] = null });
        other.Should().BeEquivalentTo(new Dictionary<string, string?> { ["view"] = "other" });
    }

    [Fact]
    public void AccountBillingEventsTab_ShouldAskForThatAccountsEventsOnlyAndPageUnderItsPrefix()
    {
        // Arrange
        var url = "https://back-office.dev.localhost:9001/blazor/back-office/accounts/42?tab=billing-events&billingEventsPageOffset=1&search=other";
        var options = new DataListUrlOptions(AccountBillingEventsListSource.DefaultOrderBy, [], parameterPrefix: AccountBillingEventsListSource.ParameterPrefix);

        // Act
        var state = DataListState.Parse(url, options);
        var query = AccountBillingEventsListSource.ToQuery(new TenantId(42), new DataListRequest(state.Filters, state.OrderBy, state.SortOrder, state.PageOffset, 25));

        // Assert
        AccountApiRoutes.BackOfficeBillingEvents(query).Should().Be("/api/back-office/billing-events?TenantId=42&OrderBy=OccurredAt&SortOrder=Descending&PageOffset=1&PageSize=25");
        BillingEventsListSource.AccountUrl(new TenantId(42)).Should().Be("/blazor/back-office/accounts/42?tab=billing-events");
    }

    [Theory]
    [InlineData(BillingEventType.SubscriptionUpgraded, SubscriptionPlan.Standard, SubscriptionPlan.Premium, SubscriptionPlan.Standard, SubscriptionPlan.Premium)]
    [InlineData(BillingEventType.SubscriptionCreated, null, SubscriptionPlan.Standard, SubscriptionPlan.Basis, SubscriptionPlan.Standard)]
    [InlineData(BillingEventType.SubscriptionCancelled, null, SubscriptionPlan.Premium, SubscriptionPlan.Premium, SubscriptionPlan.Basis)]
    public void GetPlanTransition_ShouldDefaultTheFromPlanAndFlipACancellation(BillingEventType type, SubscriptionPlan? from, SubscriptionPlan to, SubscriptionPlan expectedFrom, SubscriptionPlan expectedTo)
    {
        // Act
        var transition = BillingFormat.GetPlanTransition(type, from, to);

        // Assert
        transition.Should().Be(new PlanTransition(expectedFrom, expectedTo));
    }

    [Fact]
    public void GetPlanTransition_WhenTheEventHasNoPlanChangeOrNoToPlan_ShouldBeNull()
    {
        // Act & Assert
        BillingFormat.GetPlanTransition(BillingEventType.PaymentFailed, SubscriptionPlan.Standard, SubscriptionPlan.Standard).Should().BeNull();
        BillingFormat.GetPlanTransition(BillingEventType.SubscriptionUpgraded, SubscriptionPlan.Standard, null).Should().BeNull();
    }

    private static DataListUrlOptions CreateOptions()
    {
        string[] sortKeys = [nameof(SortableBillingEventProperties.TenantName), nameof(SortableBillingEventProperties.EventType), nameof(SortableBillingEventProperties.OccurredAt)];
        return new DataListUrlOptions(BillingEventsListSource.DefaultOrderBy, sortKeys, BillingEventsListSource.FilterParameters,
            normalizeFilters: BillingEventsListSource.NormalizeFilters, defaultSortOrder: BillingEventsListSource.DefaultSortOrder
        );
    }
}
