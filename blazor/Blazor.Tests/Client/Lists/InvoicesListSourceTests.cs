using Account.Client;
using Account.Features.BackOffice.Invoices.Queries;
using Account.Features.Subscriptions.Domain;
using Blazor.Client.BackOffice.Accounts;
using Blazor.Client.BackOffice.Billing;
using Blazor.Client.Components.Lists;
using FluentAssertions;
using SharedKernel.Domain;
using SharedKernel.Persistence;

namespace Blazor.Tests.Client.Lists;

// The back office's invoices list and the account's Invoices tab: the view is the React back office's view parameter mapped
// to its fixed status sets, the URL keeps the React names and writes back what it read, and the account API receives the
// statuses, the sort and the page.
public sealed class InvoicesListSourceTests
{
    private const string InvoicesUrl = "https://back-office.dev.localhost:9001/blazor/back-office/invoices";

    [Theory]
    [InlineData(null, InvoicesView.All)]
    [InlineData("all", InvoicesView.All)]
    [InlineData("invoices", InvoicesView.Invoices)]
    [InlineData(" refunds ", InvoicesView.Refunds)]
    [InlineData("Refunds", InvoicesView.All)]
    [InlineData("credit-notes", InvoicesView.All)]
    public void GetView_ShouldAcceptTheReactValuesOnlyWithAllAsTheFallback(string? value, InvoicesView expected)
    {
        // Arrange
        var filters = value is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [InvoicesListSource.ViewParameter] = value };

        // Act
        var view = InvoicesListSource.GetView(filters);

        // Assert
        view.Should().Be(expected);
    }

    [Theory]
    [InlineData(InvoicesView.All, new BackOfficeInvoiceStatusFilter[0])]
    [InlineData(InvoicesView.Invoices, new[] { BackOfficeInvoiceStatusFilter.Paid, BackOfficeInvoiceStatusFilter.Pending, BackOfficeInvoiceStatusFilter.Failed })]
    [InlineData(InvoicesView.Refunds, new[] { BackOfficeInvoiceStatusFilter.Refunded, BackOfficeInvoiceStatusFilter.HasCreditNote })]
    public void ToQuery_ShouldMapTheViewToTheReactStatusSet(InvoicesView view, BackOfficeInvoiceStatusFilter[] expected)
    {
        // Arrange
        var filters = new Dictionary<string, string> { [InvoicesListSource.ViewParameter] = InvoicesListSource.ToValue(view) };

        // Act
        var query = InvoicesListSource.ToQuery(new DataListRequest(filters, InvoicesListSource.DefaultOrderBy, SortOrder.Descending, 0, 25));

        // Assert
        query.Statuses.Should().Equal(expected);
    }

    [Fact]
    public void NormalizeFilters_ShouldTrimTheSearchAndLeaveOutTheDefaultAndMalformedViews()
    {
        // Act
        var all = InvoicesListSource.NormalizeFilters(new Dictionary<string, string> { ["search"] = "  acme ", ["view"] = "all" });
        var malformed = InvoicesListSource.NormalizeFilters(new Dictionary<string, string> { ["view"] = "everything" });
        var refunds = InvoicesListSource.NormalizeFilters(new Dictionary<string, string> { ["view"] = "refunds" });

        // Assert
        all.Should().BeEquivalentTo(new Dictionary<string, string> { ["search"] = "acme" });
        malformed.Should().BeEmpty();
        refunds.Should().BeEquivalentTo(new Dictionary<string, string> { ["view"] = "refunds" });
    }

    [Fact]
    public void SetView_ShouldLeaveAllOutOfTheUrl()
    {
        // Act
        var all = InvoicesListSource.SetView(InvoicesView.All);
        var invoices = InvoicesListSource.SetView(InvoicesView.Invoices);

        // Assert
        all.Should().BeEquivalentTo(new Dictionary<string, string?> { ["view"] = null });
        invoices.Should().BeEquivalentTo(new Dictionary<string, string?> { ["view"] = "invoices" });
    }

    [Fact]
    public void Parse_WhenTheUrlComesFromTheReactBackOffice_ShouldReadTheSameStateAndWriteTheSameUrl()
    {
        // Arrange: a URL as the React router writes it, sorted by total ascending on the second page of refunds
        var url = $"{InvoicesUrl}?search=acme&view=refunds&orderBy=Total&sortOrder=Ascending&pageOffset=1";

        // Act
        var state = DataListState.Parse(url, CreateOptions());
        var query = InvoicesListSource.ToQuery(new DataListRequest(state.Filters, state.OrderBy, state.SortOrder, state.PageOffset, 25));

        // Assert
        state.ToUri(url, CreateOptions()).Should().Be(url);
        AccountApiRoutes.BackOfficeInvoices(query).Should().Be(
            "/api/back-office/invoices?Search=acme&Statuses=Refunded&Statuses=HasCreditNote&OrderBy=Total&SortOrder=Ascending&PageOffset=1&PageSize=25"
        );
    }

    [Fact]
    public void Parse_WhenTheUrlHasNoSort_ShouldUseTheServerDefaultOfDateDescendingAndWriteNothing()
    {
        // Act
        var state = DataListState.Parse(InvoicesUrl, CreateOptions());
        var query = InvoicesListSource.ToQuery(new DataListRequest(state.Filters, state.OrderBy, state.SortOrder, state.PageOffset, 25));

        // Assert
        state.ToUri(InvoicesUrl, CreateOptions()).Should().Be(InvoicesUrl);
        AccountApiRoutes.BackOfficeInvoices(query).Should().Be("/api/back-office/invoices?OrderBy=Date&SortOrder=Descending&PageSize=25");
    }

    [Fact]
    public void KeyOfAndAccountUrl_ShouldKeepAReversalRowApartAndOpenTheAccountsInvoicesTab()
    {
        // Arrange
        var id = new PaymentTransactionId("pymnt_01JABCDEFGHJKMNPQRSTVWXYZ0");
        var invoice = CreateInvoice(id, BackOfficeInvoiceRowKind.Invoice);
        var creditNote = CreateInvoice(id, BackOfficeInvoiceRowKind.CreditNote);

        // Act
        var keys = new[] { InvoicesListSource.KeyOf(invoice), InvoicesListSource.KeyOf(creditNote) };

        // Assert
        keys.Should().OnlyHaveUniqueItems();
        InvoicesListSource.AccountUrl(new TenantId(42)).Should().Be("/blazor/back-office/accounts/42?tab=invoices");
    }

    [Fact]
    public void AccountInvoicesTab_ShouldPageUnderItsPrefixAndReadTheAccountsPaymentHistory()
    {
        // Arrange
        var url = "https://back-office.dev.localhost:9001/blazor/back-office/accounts/42?tab=invoices&invoicesPageOffset=2";
        var options = new DataListUrlOptions(AccountInvoicesListSource.DefaultOrderBy, [], parameterPrefix: AccountInvoicesListSource.ParameterPrefix);

        // Act
        var state = DataListState.Parse(url, options);
        var route = AccountApiRoutes.BackOfficeTenantPaymentHistory(new TenantId(42), AccountInvoicesListSource.ToQuery(new DataListRequest(state.Filters, state.OrderBy, state.SortOrder, state.PageOffset, 25)));

        // Assert
        state.PageOffset.Should().Be(2);
        route.Should().Be("/api/back-office/tenants/42/payment-history?PageOffset=2&PageSize=25");
        AccountInvoicesListSource.ListId(new TenantId(42)).Should().NotBe(AccountInvoicesListSource.ListId(new TenantId(43)));
    }

    private static DataListUrlOptions CreateOptions()
    {
        string[] sortKeys =
        [
            nameof(SortableBackOfficeInvoiceProperties.TenantName), nameof(SortableBackOfficeInvoiceProperties.Date), nameof(SortableBackOfficeInvoiceProperties.Total),
            nameof(SortableBackOfficeInvoiceProperties.Status)
        ];
        return new DataListUrlOptions(InvoicesListSource.DefaultOrderBy, sortKeys, InvoicesListSource.FilterParameters, normalizeFilters: InvoicesListSource.NormalizeFilters,
            defaultSortOrder: InvoicesListSource.DefaultSortOrder
        );
    }

    private static BackOfficeInvoiceSummary CreateInvoice(PaymentTransactionId id, BackOfficeInvoiceRowKind rowKind)
    {
        return new BackOfficeInvoiceSummary(id, rowKind, new TenantId(42), "Acme", null, DateTimeOffset.UnixEpoch, SubscriptionPlan.Standard, 100m, 80m, 20m, "DKK",
            PaymentTransactionStatus.Succeeded, null, null, null, null, null
        );
    }
}
