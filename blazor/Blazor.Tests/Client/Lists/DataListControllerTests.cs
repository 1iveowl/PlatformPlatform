using Blazor.Client.Components.Lists;
using FluentAssertions;
using SharedKernel.Persistence;

namespace Blazor.Tests.Client.Lists;

public sealed class DataListControllerTests
{
    private const string Page = "https://app.dev.localhost:9000/blazor/development/data-list";

    [Fact]
    public async Task Sort_ShouldPushAHistoryEntryResetThePageAndClearTheSelection()
    {
        // Arrange
        var (controller, browser, _) = Create($"{Page}?tab=a&pageOffset=1");
        await controller.LoadAsync();
        await controller.ToggleAsync(0);

        // Act
        await controller.SortAsync("Name");

        // Assert
        browser.History.Should().Equal(($"{Page}?tab=a&sortOrder=Descending", false));
        controller.State.Should().Be(new DataListState("Name", SortOrder.Descending, 0, null));
        controller.Selection.Keys.Should().BeEmpty();
        controller.Items[0].Should().Be("row-059");
    }

    [Fact]
    public async Task GoToPage_ShouldPushAHistoryEntryAndClearTheSelection()
    {
        // Arrange
        var (controller, browser, server) = Create(Page);
        await controller.LoadAsync();
        await controller.ToggleAllAsync();

        // Act
        await controller.GoToPageAsync(1);

        // Assert
        browser.History.Should().Equal(($"{Page}?pageOffset=1", false));
        controller.Selection.Keys.Should().BeEmpty();
        controller.Items[0].Should().Be("row-025");
        server.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task SetFilters_ShouldReplaceTheHistoryEntryResetThePageAndClearTheSelection()
    {
        // Arrange
        var (controller, browser, _) = Create($"{Page}?pageOffset=2&orderBy=Name&sortOrder=Descending");
        await controller.LoadAsync();
        await controller.ToggleAsync(1);

        // Act
        await controller.SetFiltersAsync(new Dictionary<string, string?> { ["search"] = "x" });

        // Assert
        browser.History.Should().Equal(($"{Page}?search=x&sortOrder=Descending", true));
        controller.Selection.Keys.Should().BeEmpty();
        controller.Items[0].Should().Be("row-059x");
    }

    [Fact]
    public async Task SetFilters_WhenADateRangeIsSetAndCleared_ShouldWriteBothBoundsFetchThemAndRemoveThemTogether()
    {
        // Arrange
        var browser = new FakeBrowser { Uri = $"{Page}?userRole=Admin&pageOffset=1" };
        var requests = new List<DataListRequest>();
        var options = new DataListUrlOptions("Name", ["Name"], ["search", "userRole", "startDate", "endDate"], "userId");
        var controller = new DataListController<string>(new DataListPageCache(), options, DataListSelectionMode.Multiple, row => row, () => browser.Uri, browser.Navigate)
        {
            ListId = "rows", CacheScope = "tnt_1/usr_1", Fetch = (request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(DataListFetchResult<string>.Success(["row-000", "row-001"], 30));
            }
        };
        await controller.LoadAsync();

        // Act
        await controller.SetFiltersAsync(new Dictionary<string, string?> { ["startDate"] = "2026-01-01", ["endDate"] = "2026-01-31" });
        var rangeUri = browser.Uri;
        await controller.SetFiltersAsync(new Dictionary<string, string?> { ["startDate"] = null, ["endDate"] = null });

        // Assert
        rangeUri.Should().Be($"{Page}?userRole=Admin&startDate=2026-01-01&endDate=2026-01-31");
        requests[1].Filters.Should().BeEquivalentTo(new Dictionary<string, string> { ["userRole"] = "Admin", ["startDate"] = "2026-01-01", ["endDate"] = "2026-01-31" });
        requests[1].PageOffset.Should().Be(0);
        browser.Uri.Should().Be($"{Page}?userRole=Admin");
        browser.History.Should().OnlyContain(entry => entry.Replace);
    }

    [Fact]
    public async Task LocationChanged_WhenBackRestoresAnEarlierState_ShouldLoadItFromTheCacheAndIgnoreOwnNavigations()
    {
        // Arrange
        var (controller, browser, server) = Create(Page);
        await controller.LoadAsync();
        await controller.GoToPageAsync(1);
        var ownNavigationChanged = await controller.LocationChangedAsync(browser.Uri);

        // Act
        browser.Uri = Page;
        var backChanged = await controller.LocationChangedAsync(Page);

        // Assert
        ownNavigationChanged.Should().BeFalse();
        backChanged.Should().BeTrue();
        controller.State.PageOffset.Should().Be(0);
        controller.Items[0].Should().Be("row-000");
        server.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Load_WhenDeepLinkOffsetIsStale_ShouldShowTheLastPageAndReplaceTheUrl()
    {
        // Arrange
        var (controller, browser, _) = Create($"{Page}?search=&pageOffset=7&userId=row-001");

        // Act
        await controller.LoadAsync();

        // Assert
        controller.Status.Should().Be(DataListStatus.Ready);
        controller.ErrorMessage.Should().BeNull();
        controller.State.PageOffset.Should().Be(2);
        browser.History.Should().Equal(($"{Page}?pageOffset=2&userId=row-001", true));
    }

    [Fact]
    public async Task Load_WhenResultIsEmpty_ShouldShowTheEmptyStateOnTheFirstPage()
    {
        // Arrange
        var (controller, _, server) = Create($"{Page}?pageOffset=3");
        server.RowCount = 0;

        // Act
        await controller.LoadAsync();

        // Assert
        controller.Status.Should().Be(DataListStatus.Empty);
        controller.State.PageOffset.Should().Be(0);
        controller.TotalPages.Should().Be(0);
    }

    [Fact]
    public async Task Load_WhenAnOlderSearchCompletesLast_ShouldKeepTheCurrentView()
    {
        // Arrange
        var (controller, browser, server) = Create(Page);
        server.Hold(1);
        var olderSearch = controller.SetFiltersAsync(new Dictionary<string, string?> { ["search"] = "old" });
        await controller.SetFiltersAsync(new Dictionary<string, string?> { ["search"] = "new" });

        // Act
        server.Release(1);
        await olderSearch;

        // Assert
        controller.Items[0].Should().Be("row-000new");
        controller.State.Filters["search"].Should().Be("new");
        browser.Uri.Should().Be($"{Page}?search=new");
    }

    [Fact]
    public async Task Invalidate_AfterAMutation_ShouldRefetchEveryVariantAndClearTheSelection()
    {
        // Arrange
        var (controller, _, server) = Create(Page);
        await controller.LoadAsync();
        await controller.SortAsync("Name");
        await controller.ToggleAllAsync();
        server.RowCount = 58;

        // Act
        await controller.InvalidateAsync();
        await controller.SortAsync("Name");

        // Assert
        controller.Selection.Keys.Should().BeEmpty();
        controller.TotalCount.Should().Be(58);
        server.Requests.Should().HaveCount(4);
    }

    [Fact]
    public async Task Click_ShouldActivateWithTheSelectedKeyAndRaiseMultipleSelection()
    {
        // Arrange
        var (controller, browser, _) = Create(Page);
        var events = new List<(int Count, bool IsMultiple)>();
        controller.SelectionChanged = (keys, isMultiple) =>
        {
            events.Add((keys.Count, isMultiple));
            return Task.CompletedTask;
        };
        await controller.LoadAsync();

        // Act
        var plain = await controller.ClickAsync(2, false, false);
        var range = await controller.ClickAsync(4, false, true);
        var closed = controller.Close();

        // Assert
        plain.Should().Be((true, "row-002"));
        range.Activated.Should().BeFalse();
        events.Should().Equal((1, false), (3, true));
        closed.Should().BeTrue();
        browser.History.Should().Equal(($"{Page}?userId=row-002", true), (Page, true));
        controller.ActiveIndex.Should().Be(-1);
    }

    [Fact]
    public async Task ToggleAll_ShouldSelectThePageAndReportAMultipleSelection()
    {
        // Arrange
        var (controller, _, _) = Create(Page);
        await controller.LoadAsync();
        var changes = new List<(int Count, bool IsMultiple)>();
        controller.SelectionChanged = (keys, isMultiple) =>
        {
            changes.Add((keys.Count, isMultiple));
            return Task.CompletedTask;
        };

        // Act
        await controller.ToggleAllAsync();

        // Assert
        controller.Selection.Keys.Should().HaveCount(DataListController<string>.PageSize);
        controller.Selection.GetHeaderSelection(controller.PageKeys).Should().Be(DataListHeaderSelection.All);
        changes.Should().Equal((DataListController<string>.PageSize, true));
    }

    [Theory]
    [InlineData("sort")]
    [InlineData("page")]
    [InlineData("filter")]
    public async Task SelectAll_WhenSortPageOrFilterChanges_ShouldReportTheClearedSelection(string change)
    {
        // Arrange
        var (controller, _, _) = Create(Page);
        await controller.LoadAsync();
        await controller.ToggleAllAsync();
        var changes = new List<(int Count, bool IsMultiple)>();
        controller.SelectionChanged = (keys, isMultiple) =>
        {
            changes.Add((keys.Count, isMultiple));
            return Task.CompletedTask;
        };

        // Act
        await (change switch
        {
            "sort" => controller.SortAsync("Name"),
            "page" => controller.GoToPageAsync(1),
            _ => controller.SetFiltersAsync(new Dictionary<string, string?> { ["search"] = "row" })
        });

        // Assert
        controller.Selection.Keys.Should().BeEmpty();
        changes.Should().Equal((0, false));
    }

    [Fact]
    public async Task SetLoadMode_WhenInfiniteAndTheRangeIsCached_ShouldRestoreEveryPageWithoutFetching()
    {
        // Arrange
        var cache = new DataListPageCache();
        var (browsing, _, _) = Create(Page, cache: cache);
        await browsing.LoadAsync();
        await browsing.GoToPageAsync(1);
        await browsing.GoToPageAsync(2);
        var (controller, browser, server) = Create($"{Page}?pageOffset=2", cache: cache);

        // Act
        await controller.SetLoadModeAsync(DataListLoadMode.Infinite);

        // Assert
        server.Requests.Should().BeEmpty();
        controller.Items.Should().HaveCount(60);
        controller.State.PageOffset.Should().Be(2);
        controller.HasMore.Should().BeFalse();
        browser.History.Should().BeEmpty();
    }

    [Fact]
    public async Task SetLoadMode_WhenInfiniteAfterCacheEvictionWithALargeOffset_ShouldFetchAtMostTheBudgetAndCorrectTheOffset()
    {
        // Arrange
        var (controller, browser, server) = Create($"{Page}?pageOffset=9", 300);

        // Act
        await controller.SetLoadModeAsync(DataListLoadMode.Infinite);

        // Assert
        server.Requests.Select(request => request.PageOffset).Should().Equal(0, 1, 2, 3);
        controller.Items.Should().HaveCount(4 * DataListController<string>.PageSize);
        controller.State.PageOffset.Should().Be(3);
        controller.HasMore.Should().BeTrue();
        browser.History.Should().Equal(($"{Page}?pageOffset=3", true));
    }

    [Fact]
    public async Task SetLoadMode_WhenInfiniteWithAnOffsetBeyondTheEnd_ShouldStopAtTheLastPage()
    {
        // Arrange
        var (controller, browser, server) = Create($"{Page}?pageOffset=50");

        // Act
        await controller.SetLoadModeAsync(DataListLoadMode.Infinite);

        // Assert
        server.Requests.Should().HaveCount(3);
        controller.Items.Should().HaveCount(60);
        controller.HasMore.Should().BeFalse();
        browser.Uri.Should().Be($"{Page}?pageOffset=2");
    }

    [Fact]
    public async Task LoadNext_ShouldAppendPagesReplaceTheOffsetAndStopAtTheEnd()
    {
        // Arrange
        var (controller, browser, server) = Create(Page);
        await controller.SetLoadModeAsync(DataListLoadMode.Infinite);

        // Act
        var first = await controller.LoadNextAsync();
        var second = await controller.LoadNextAsync();
        var afterEnd = await controller.LoadNextAsync();

        // Assert
        (first, second, afterEnd).Should().Be((true, true, false));
        controller.Items.Should().HaveCount(60);
        controller.Items[59].Should().Be("row-059");
        controller.HasMore.Should().BeFalse();
        server.Requests.Should().HaveCount(3);
        browser.History.Should().Equal(($"{Page}?pageOffset=1", true), ($"{Page}?pageOffset=2", true));
    }

    [Fact]
    public async Task LoadNext_WhenAPageRepeatsLoadedRows_ShouldAppendEachRowOnce()
    {
        // Arrange
        var (controller, _, server) = Create(Page);
        controller.Fetch = async (request, cancellationToken) =>
        {
            var result = await server.FetchAsync(request, cancellationToken);
            // A row inserted before the first page shifts row-024 onto the second page as well
            return request.PageOffset == 1 ? DataListFetchResult<string>.Success(["row-024", .. result.Page!.Items], result.Page.TotalCount) : result;
        };
        await controller.SetLoadModeAsync(DataListLoadMode.Infinite);

        // Act
        await controller.LoadNextAsync();

        // Assert
        controller.Items.Should().OnlyHaveUniqueItems();
        controller.Items.Should().HaveCount(50);
    }

    [Fact]
    public async Task LoadNext_WhenRequestedWhileOneIsInFlight_ShouldIgnoreTheSecondRequest()
    {
        // Arrange
        var (controller, _, server) = Create(Page);
        await controller.SetLoadModeAsync(DataListLoadMode.Infinite);
        server.Hold(2);

        // Act
        var pending = controller.LoadNextAsync();
        var duplicate = await controller.LoadNextAsync();
        server.Release(2);
        await pending;

        // Assert
        duplicate.Should().BeFalse();
        server.Requests.Should().HaveCount(2);
        controller.Items.Should().HaveCount(50);
    }

    [Fact]
    public async Task LoadNext_WhenThePageFails_ShouldKeepTheRowsAndLoadItOnRetry()
    {
        // Arrange
        var (controller, browser, server) = Create(Page);
        await controller.SetLoadModeAsync(DataListLoadMode.Infinite);
        server.FailWithStatus = 500;

        // Act
        await controller.LoadNextAsync();
        var failed = (controller.NextPageErrorMessage, controller.Items.Count, controller.HasMore);
        server.FailWithStatus = null;
        await controller.LoadNextAsync();

        // Assert
        failed.NextPageErrorMessage.Should().NotBeNull();
        failed.Count.Should().Be(25);
        failed.HasMore.Should().BeTrue();
        controller.NextPageErrorMessage.Should().BeNull();
        controller.Items.Should().HaveCount(50);
        browser.Uri.Should().Be($"{Page}?pageOffset=1");
    }

    [Fact]
    public async Task LoadNext_WhenTheFilterChangesWhileAPageIsInFlight_ShouldDiscardThePageAndStartOver()
    {
        // Arrange
        var (controller, browser, server) = Create(Page);
        await controller.SetLoadModeAsync(DataListLoadMode.Infinite);
        await controller.LoadNextAsync();
        server.Hold(3);

        // Act
        var pending = controller.LoadNextAsync();
        await controller.SetFiltersAsync(new Dictionary<string, string?> { ["search"] = "x" });
        await pending;

        // Assert
        controller.Items.Should().HaveCount(25);
        controller.Items[0].Should().Be("row-000x");
        controller.State.PageOffset.Should().Be(0);
        controller.IsLoadingNext.Should().BeFalse();
        browser.Uri.Should().Be($"{Page}?search=x");
    }

    [Fact]
    public async Task SetFilters_WhenInfiniteAndAnOlderSearchCompletesLast_ShouldKeepTheCurrentRange()
    {
        // Arrange
        var (controller, _, server) = Create(Page);
        await controller.SetLoadModeAsync(DataListLoadMode.Infinite);
        server.Hold(2);
        var olderSearch = controller.SetFiltersAsync(new Dictionary<string, string?> { ["search"] = "old" });
        await controller.SetFiltersAsync(new Dictionary<string, string?> { ["search"] = "new" });

        // Act
        server.Release(2);
        await olderSearch;

        // Assert
        controller.Items.Should().HaveCount(25);
        controller.Items.Should().OnlyContain(row => row.EndsWith("new"));
    }

    [Fact]
    public async Task SetLoadMode_WhenSwitchingAfterSeveralPages_ShouldKeepTheOffsetAndTheActivatedRowAndDeselectUnloadedRows()
    {
        // Arrange
        var (controller, _, server) = Create(Page);
        await controller.SetLoadModeAsync(DataListLoadMode.Infinite);
        await controller.LoadNextAsync();
        await controller.LoadNextAsync();
        await controller.ClickAsync(55, false, false);
        await controller.ToggleAsync(1);

        // Act
        await controller.SetLoadModeAsync(DataListLoadMode.Pages);
        var pages = (controller.Items[0], controller.ActiveIndex, controller.Selection.Keys.ToArray());
        await controller.SetLoadModeAsync(DataListLoadMode.Infinite);

        // Assert
        pages.Item1.Should().Be("row-050");
        pages.ActiveIndex.Should().Be(5);
        pages.Item3.Should().Equal("row-055");
        controller.State.PageOffset.Should().Be(2);
        controller.Items.Should().HaveCount(60);
        controller.ActiveIndex.Should().Be(55);
        server.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task ToggleAll_WhenMoreRowsThanTheBulkLimitAreLoaded_ShouldSelectOnlyTheLimit()
    {
        // Arrange
        var (controller, _, _) = Create(Page, 300);
        await controller.SetLoadModeAsync(DataListLoadMode.Infinite);
        for (var page = 0; page < 4; page++)
        {
            await controller.LoadNextAsync();
        }

        // Act
        await controller.ToggleAllAsync();

        // Assert
        controller.Items.Should().HaveCount(125);
        controller.Selection.Keys.Should().HaveCount(DataListController<string>.MaxSelectedKeys);
        controller.Selection.Keys.Should().NotContain("row-100");
    }

    private static (DataListController<string> Controller, FakeBrowser Browser, FakeListServer Server) Create(string uri, int rowCount = 60, DataListPageCache? cache = null)
    {
        var browser = new FakeBrowser { Uri = uri };
        var server = new FakeListServer(rowCount);
        var options = new DataListUrlOptions("Name", ["Name"], ["search"], "userId");
        var controller = new DataListController<string>(cache ?? new DataListPageCache(), options, DataListSelectionMode.Multiple, row => row, () => browser.Uri, browser.Navigate)
        {
            ListId = "rows", CacheScope = "tnt_1/usr_1", Fetch = server.FetchAsync
        };
        return (controller, browser, server);
    }

    private sealed class FakeBrowser
    {
        public string Uri { get; set; } = "";

        public List<(string Uri, bool Replace)> History { get; } = [];

        public void Navigate(string uri, bool replace)
        {
            Uri = uri;
            History.Add((uri, replace));
        }
    }
}
