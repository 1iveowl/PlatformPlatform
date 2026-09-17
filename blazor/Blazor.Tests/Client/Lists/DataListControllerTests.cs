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

    private static (DataListController<string> Controller, FakeBrowser Browser, FakeListServer Server) Create(string uri)
    {
        var browser = new FakeBrowser { Uri = uri };
        var server = new FakeListServer(60);
        var options = new DataListUrlOptions("Name", ["Name"], ["search"], "userId");
        var controller = new DataListController<string>(new DataListPageCache(), options, DataListSelectionMode.Multiple, row => row, () => browser.Uri, browser.Navigate)
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
