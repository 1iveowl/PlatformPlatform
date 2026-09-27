using System.Net;
using System.Text;
using Account.Client;
using Account.Features.Subscriptions.Domain;
using Account.Features.Tenants.BackOffice.Queries;
using Blazor.Client.BackOffice.Accounts;
using Blazor.Client.Components.Lists;
using FluentAssertions;
using SharedKernel.Persistence;

namespace Blazor.Tests.Client.Lists;

// The back office's accounts list keeps the React back office's URL: the same parameter names, plans and statuses as the JSON
// arrays the React router writes, no orderBy for the server's default order by last modification, and a descending default
// with sortOrder written only for Ascending. It sends the tenants query the React back office sends.
public sealed class AccountsListSourceTests
{
    private const string AccountsUrl = "https://back-office.dev.localhost:9001/blazor/back-office/accounts";

    public static TheoryData<Dictionary<string, string>, string, SortOrder, int, string[]> QueryCases => new()
    {
        { new Dictionary<string, string>(), "ModifiedAt", SortOrder.Descending, 0, ["OrderBy=ModifiedAt", "SortOrder=Descending", "PageSize=25"] },
        { new Dictionary<string, string>(), "Name", SortOrder.Ascending, 2, ["OrderBy=Name", "SortOrder=Ascending", "PageOffset=2", "PageSize=25"] },
        {
            new Dictionary<string, string>
            {
                ["search"] = "a b&c", ["plans"] = """["Premium","Basis"]""", ["statuses"] = """["Active","Downgrading"]""", ["unsynced"] = "true", ["driftDetected"] = "true"
            },
            "MonthlyRecurringRevenue", SortOrder.Descending, 1,
            [
                "Search=a%20b%26c", "Plans=Premium", "Plans=Basis", "Statuses=Active", "Statuses=Downgrading", "Unsynced=true", "DriftDetected=true",
                "OrderBy=MonthlyRecurringRevenue", "SortOrder=Descending", "PageOffset=1", "PageSize=25"
            ]
        }
    };

    [Theory]
    [MemberData(nameof(QueryCases))]
    public async Task Fetch_ShouldSendTheTenantsQuery(Dictionary<string, string> filters, string orderBy, SortOrder sortOrder, int pageOffset, string[] expectedParameters)
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.OK, """{"totalCount":0,"pageSize":25,"totalPages":0,"currentPageOffset":0,"tenants":[]}""");
        var backOfficeClient = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await AccountsListSource.FetchAsync(backOfficeClient, new DataListRequest(filters, orderBy, sortOrder, pageOffset, DataListController<TenantSummary>.PageSize),
            CancellationToken.None
        );

        // Assert
        result.Page.Should().NotBeNull();
        var request = network.Requests.Should().ContainSingle().Subject;
        request.AbsolutePath.Should().Be("/api/back-office/tenants");
        request.Query.TrimStart('?').Split('&').Should().BeEquivalentTo(expectedParameters);
    }

    [Fact]
    public async Task Fetch_WhenTheApiRejectsTheRequest_ShouldReturnTheStatusAndDetail()
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.BadRequest, """{"title":"Bad Request","status":400,"detail":"The page offset '9' is greater than the total number of pages."}""");
        var backOfficeClient = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });
        var request = new DataListRequest(new Dictionary<string, string>(), "ModifiedAt", SortOrder.Descending, 9, DataListController<TenantSummary>.PageSize);

        // Act
        var result = await AccountsListSource.FetchAsync(backOfficeClient, request, CancellationToken.None);

        // Assert
        result.Page.Should().BeNull();
        result.StatusCode.Should().Be(400);
        result.ErrorMessage.Should().Be("The page offset '9' is greater than the total number of pages.");
    }

    [Fact]
    public void NormalizeFilters_WhenValuesAreMalformed_ShouldKeepOnlyValidValuesInCanonicalForm()
    {
        // Arrange
        var filters = new Dictionary<string, string>
        {
            ["search"] = "  acme  ", ["plans"] = """[ "basis" , "Unknown", "Premium", "Premium" ]""", ["statuses"] = "[Active]", ["unsynced"] = "false",
            ["driftDetected"] = "TRUE"
        };

        // Act
        var normalized = AccountsListSource.NormalizeFilters(filters);

        // Assert
        normalized.Should().BeEquivalentTo(new Dictionary<string, string> { ["search"] = "acme", ["plans"] = """["Premium","Basis"]""", ["driftDetected"] = "true" });
    }

    [Fact]
    public void NormalizeFilters_WhenAMultiValueFilterIsASingleBareName_ShouldKeepItAsAJsonArray()
    {
        // Arrange
        var filters = new Dictionary<string, string> { ["statuses"] = "Canceling", ["plans"] = "3" };

        // Act
        var normalized = AccountsListSource.NormalizeFilters(filters);

        // Assert
        normalized.Should().BeEquivalentTo(new Dictionary<string, string> { ["statuses"] = """["Canceling"]""" });
    }

    [Fact]
    public void TogglePlan_ShouldAddAndRemoveThePlanInTheToolbarOrderAndDropTheParameterWhenNoneRemains()
    {
        // Arrange
        var filters = new Dictionary<string, string> { ["plans"] = """["Basis"]""" };

        // Act
        var added = AccountsListSource.TogglePlan(filters, SubscriptionPlan.Premium);
        var removed = AccountsListSource.TogglePlan(filters, SubscriptionPlan.Basis);

        // Assert
        added.Should().BeEquivalentTo(new Dictionary<string, string?> { ["plans"] = """["Premium","Basis"]""" });
        removed.Should().BeEquivalentTo(new Dictionary<string, string?> { ["plans"] = null });
    }

    [Fact]
    public void ToggleStatus_ShouldKeepTheOtherStatuses()
    {
        // Arrange
        var filters = new Dictionary<string, string> { ["statuses"] = """["Active","Free"]""" };

        // Act
        var changes = AccountsListSource.ToggleStatus(filters, TenantStatusFilter.Canceled);

        // Assert
        changes.Should().BeEquivalentTo(new Dictionary<string, string?> { ["statuses"] = """["Active","Canceled","Free"]""" });
    }

    [Fact]
    public void ToUrl_ShouldLinkIntoTheListWithTheFiltersInTheirUrlForm()
    {
        // Act
        var drift = AccountsListSource.ToUrl(new Dictionary<string, string> { ["driftDetected"] = "true" });
        var statuses = AccountsListSource.ToUrl([TenantStatusFilter.Active, TenantStatusFilter.Downgrading]);

        // Assert
        drift.Should().Be("/blazor/back-office/accounts?driftDetected=true");
        statuses.Should().Be("/blazor/back-office/accounts?statuses=%5B%22Active%22%2C%22Downgrading%22%5D");
    }

    [Fact]
    public void Parse_WhenTheUrlHasNoSort_ShouldUseTheServerDefaultOrderByLastModificationDescending()
    {
        // Act
        var state = DataListState.Parse(AccountsUrl, CreateOptions());

        // Assert
        state.OrderBy.Should().Be("ModifiedAt");
        state.SortOrder.Should().Be(SortOrder.Descending);
        state.ToUri(AccountsUrl, CreateOptions()).Should().Be(AccountsUrl);
    }

    [Fact]
    public void Parse_WhenTheUrlComesFromTheReactBackOffice_ShouldReadTheSameStateAndWriteTheSameUrl()
    {
        // Arrange: a URL as the React router writes it, sorted by signup date in its default (descending) order
        var url = $"{AccountsUrl}?search=acme&plans=%5B%22Premium%22%2C%22Standard%22%5D&statuses=%5B%22Active%22%5D&unsynced=true&driftDetected=true&orderBy=CreatedAt&pageOffset=1";

        // Act
        var state = DataListState.Parse(url, CreateOptions());
        var written = state.ToUri(url, CreateOptions());

        // Assert
        state.OrderBy.Should().Be("CreatedAt");
        state.SortOrder.Should().Be(SortOrder.Descending);
        state.PageOffset.Should().Be(1);
        state.Filters.Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["search"] = "acme", ["plans"] = """["Premium","Standard"]""", ["statuses"] = """["Active"]""", ["unsynced"] = "true", ["driftDetected"] = "true"
            }
        );
        written.Should().Be(url);
    }

    [Fact]
    public void Parse_WhenTheUrlAsksForAscending_ShouldWriteTheSortOrderBack()
    {
        // Arrange
        var url = $"{AccountsUrl}?orderBy=Name&sortOrder=Ascending";

        // Act
        var state = DataListState.Parse(url, CreateOptions());

        // Assert
        state.SortOrder.Should().Be(SortOrder.Ascending);
        state.ToUri(url, CreateOptions()).Should().Be(url);
    }

    [Fact]
    public async Task Sort_ShouldStartANewColumnDescendingAndThenToggleAsTheReactBackOfficeDoes()
    {
        // Arrange
        var browser = new FakeBrowser { Uri = AccountsUrl };
        var controller = new DataListController<string>(new DataListPageCache(), CreateOptions(), DataListSelectionMode.Single, row => row, () => browser.Uri, browser.Navigate)
        {
            ListId = AccountsListSource.ListId, CacheScope = AccountsListSource.CacheScope, Fetch = new FakeListServer(10).FetchAsync
        };
        await controller.LoadAsync();

        // Act
        await controller.SortAsync("Name");
        var first = browser.Uri;
        await controller.SortAsync("Name");
        var second = browser.Uri;
        await controller.SortAsync("Name");
        var third = browser.Uri;

        // Assert
        first.Should().Be($"{AccountsUrl}?orderBy=Name");
        second.Should().Be($"{AccountsUrl}?orderBy=Name&sortOrder=Ascending");
        third.Should().Be($"{AccountsUrl}?orderBy=Name");
        controller.State.SortOrder.Should().Be(SortOrder.Descending);
    }

    [Fact]
    public async Task Load_WhenTheUrlNamesTheHiddenDefaultKey_ShouldReadTheKeyAndItsOrder()
    {
        // Arrange
        var browser = new FakeBrowser { Uri = $"{AccountsUrl}?orderBy=ModifiedAt&sortOrder=Ascending" };
        var controller = new DataListController<string>(new DataListPageCache(), CreateOptions(), DataListSelectionMode.Single, row => row, () => browser.Uri, browser.Navigate)
        {
            ListId = AccountsListSource.ListId, CacheScope = AccountsListSource.CacheScope, Fetch = new FakeListServer(10).FetchAsync
        };

        // Act
        await controller.LoadAsync();

        // Assert
        controller.State.OrderBy.Should().Be("ModifiedAt");
        controller.State.SortOrder.Should().Be(SortOrder.Ascending);
    }

    // The options DataList builds for the accounts list: its column sort keys, the hidden default key and the default order
    private static DataListUrlOptions CreateOptions()
    {
        string[] columnKeys =
        [
            nameof(SortableTenantProperties.Name), nameof(SortableTenantProperties.Plan), nameof(SortableTenantProperties.MonthlyRecurringRevenue),
            nameof(SortableTenantProperties.RenewalDate), nameof(SortableTenantProperties.Status), nameof(SortableTenantProperties.Country),
            nameof(SortableTenantProperties.CreatedAt)
        ];
        return new DataListUrlOptions(AccountsListSource.DefaultOrderBy, [.. columnKeys, .. AccountsListSource.HiddenSortKeys], AccountsListSource.FilterParameters,
            AccountsListSource.SelectedKeyParameter, "", AccountsListSource.NormalizeFilters, AccountsListSource.DefaultSortOrder
        );
    }

    private sealed class FakeBrowser
    {
        public string Uri { get; set; } = "";

        public void Navigate(string uri, bool replace)
        {
            Uri = uri;
        }
    }

    private sealed class RecordingNetwork(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var mediaType = statusCode == HttpStatusCode.OK ? "application/json" : "application/problem+json";
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
        }
    }
}
