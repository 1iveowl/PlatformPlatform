using Account.Features.BackOffice.BillingEvents.Queries;
using Account.Features.BackOffice.Invoices.Queries;
using Account.Features.FeatureFlags.Queries;
using Account.Features.Subscriptions.Domain;
using Account.Features.Tenants.BackOffice.Queries;
using Account.Features.Users.Domain;
using Blazor.Client.BackOffice.Accounts;
using Blazor.Client.BackOffice.Billing;
using Blazor.Client.BackOffice.FeatureFlags;
using Blazor.Client.Components.Lists;
using Blazor.Client.Users;
using FluentAssertions;
using SharedKernel.Persistence;

namespace Blazor.Tests.Client.Lists;

// The value forms the list sources share: enum names, never numbers, matched with the case rule each source states, and
// multi-value filters as the JSON arrays of names the React router writes, distinct and in the source's canonical order.
public sealed class DataListQueryValuesTests
{
    private static readonly SubscriptionPlan[] CanonicalOrder = [SubscriptionPlan.Premium, SubscriptionPlan.Standard, SubscriptionPlan.Basis];

    public static TheoryData<string?, SubscriptionPlan[]> ParseValuesCases => new()
    {
        { null, [] },
        { "", [] },
        { "   ", [] },
        { "[]", [] },
        { """["Basis","Premium"]""", [SubscriptionPlan.Premium, SubscriptionPlan.Basis] },
        { """["Premium","premium","PREMIUM","Premium"]""", [SubscriptionPlan.Premium] },
        { """  [ "Standard" , ,"Gold" ]  """, [SubscriptionPlan.Standard] },
        { """["2","-1","99","Basis"]""", [SubscriptionPlan.Basis] },
        { """[Premium,"Standard"]""", [SubscriptionPlan.Standard] },
        { """[" Premium"]""", [] },
        { "Standard", [SubscriptionPlan.Standard] },
        { " standard ", [SubscriptionPlan.Standard] },
        { "2", [] },
        { "Gold", [] }
    };

    [Theory]
    [InlineData("Premium", StringComparison.Ordinal, SubscriptionPlan.Premium)]
    [InlineData("premium", StringComparison.Ordinal, null)]
    [InlineData("premium", StringComparison.OrdinalIgnoreCase, SubscriptionPlan.Premium)]
    [InlineData("PREMIUM", StringComparison.OrdinalIgnoreCase, SubscriptionPlan.Premium)]
    [InlineData("2", StringComparison.Ordinal, null)]
    [InlineData("2", StringComparison.OrdinalIgnoreCase, null)]
    [InlineData("-1", StringComparison.OrdinalIgnoreCase, null)]
    [InlineData("99", StringComparison.OrdinalIgnoreCase, null)]
    [InlineData(" Premium", StringComparison.OrdinalIgnoreCase, null)]
    [InlineData("Premium ", StringComparison.Ordinal, null)]
    [InlineData("Gold", StringComparison.OrdinalIgnoreCase, null)]
    [InlineData("", StringComparison.OrdinalIgnoreCase, null)]
    [InlineData(null, StringComparison.OrdinalIgnoreCase, null)]
    public void ParseName_ShouldAcceptOnlyNamesWithTheStatedCaseRule(string? value, StringComparison comparison, SubscriptionPlan? expected)
    {
        // Act
        var plan = DataListQueryValues.ParseName<SubscriptionPlan>(value, comparison);

        // Assert
        plan.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(ParseValuesCases))]
    public void ParseValues_ShouldKeepKnownNamesOnceInTheCanonicalOrder(string? value, SubscriptionPlan[] expected)
    {
        // Act
        var plans = DataListQueryValues.ParseValues(value, CanonicalOrder);

        // Assert
        plans.Should().Equal(expected);
    }

    [Fact]
    public void ParseValues_WhenANameIsOutsideTheCanonicalOrder_ShouldDropIt()
    {
        // Act
        var plans = DataListQueryValues.ParseValues("""["Basis","Standard"]""", [SubscriptionPlan.Premium, SubscriptionPlan.Standard]);

        // Assert
        plans.Should().Equal(SubscriptionPlan.Standard);
    }

    [Fact]
    public void FormatValues_ShouldWriteAJsonArrayOfNamesOrNothing()
    {
        // Act
        var empty = DataListQueryValues.FormatValues<SubscriptionPlan>([]);
        var plans = DataListQueryValues.FormatValues([SubscriptionPlan.Premium, SubscriptionPlan.Basis]);

        // Assert
        empty.Should().BeNull();
        plans.Should().Be("""["Premium","Basis"]""");
    }

    [Fact]
    public void FormatValues_WhenReadBack_ShouldRoundTripEverySelection()
    {
        for (var mask = 1; mask < 1 << CanonicalOrder.Length; mask++)
        {
            // Arrange
            var selection = CanonicalOrder.Where((_, index) => (mask & (1 << index)) != 0).ToArray();

            // Act
            var written = DataListQueryValues.FormatValues(selection);
            var read = DataListQueryValues.ParseValues(written, CanonicalOrder);

            // Assert
            read.Should().Equal(selection);
            DataListQueryValues.FormatValues(read).Should().Be(written);
        }
    }

    [Fact]
    public void Toggle_ShouldAddInTheCanonicalOrderAndRemoveDownToNothing()
    {
        // Act
        var added = DataListQueryValues.Toggle([SubscriptionPlan.Basis], SubscriptionPlan.Premium, CanonicalOrder);
        var removed = DataListQueryValues.Toggle(added, SubscriptionPlan.Basis, CanonicalOrder);
        var none = DataListQueryValues.Toggle(removed, SubscriptionPlan.Premium, CanonicalOrder);

        // Assert
        added.Should().Equal(SubscriptionPlan.Premium, SubscriptionPlan.Basis);
        removed.Should().Equal(SubscriptionPlan.Premium);
        none.Should().BeEmpty();
        DataListQueryValues.FormatValues(none).Should().BeNull();
    }

    [Fact]
    public void ToQuery_ShouldKeepEachSourcesCaseRuleForTheSortKey()
    {
        // Act
        var accounts = AccountsListSource.ToQuery(Request("name"));
        var users = UsersListSource.ToQuery(Request("email"));
        var flagUsers = FeatureFlagUsersListSource.ToQuery(Request("tenantName"));
        var invoices = InvoicesListSource.ToQuery(Request("total"));
        var invoicesExact = InvoicesListSource.ToQuery(Request("Total"));
        var billingEvents = BillingEventsListSource.ToQuery(Request("tenantName"));
        var billingEventsExact = BillingEventsListSource.ToQuery(Request("TenantName"));
        var numericAccounts = AccountsListSource.ToQuery(Request("0"));
        var numericInvoices = InvoicesListSource.ToQuery(Request("2"));

        // Assert
        accounts.OrderBy.Should().Be(SortableTenantProperties.Name);
        users.OrderBy.Should().Be(SortableUserProperties.Email);
        flagUsers.OrderBy.Should().Be(SortableFeatureFlagUserProperties.TenantName);
        invoices.OrderBy.Should().Be(SortableBackOfficeInvoiceProperties.Date);
        invoicesExact.OrderBy.Should().Be(SortableBackOfficeInvoiceProperties.Total);
        billingEvents.OrderBy.Should().Be(SortableBillingEventProperties.OccurredAt);
        billingEventsExact.OrderBy.Should().Be(SortableBillingEventProperties.TenantName);
        numericAccounts.OrderBy.Should().Be(SortableTenantProperties.ModifiedAt);
        numericInvoices.OrderBy.Should().Be(SortableBackOfficeInvoiceProperties.Date);
    }

    private static DataListRequest Request(string orderBy)
    {
        return new DataListRequest(new Dictionary<string, string>(), orderBy, SortOrder.Ascending, 0, 25);
    }
}
