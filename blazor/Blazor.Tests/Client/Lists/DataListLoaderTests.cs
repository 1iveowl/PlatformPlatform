using Blazor.Client.Components.Lists;
using FluentAssertions;
using SharedKernel.Persistence;

namespace Blazor.Tests.Client.Lists;

public sealed class DataListLoaderTests
{
    private const string Scope = "tnt_1/usr_1";
    private const string ListId = "rows";
    private const int PageSize = 25;

    [Fact]
    public async Task Load_WhenPageWasLoadedBefore_ShouldMakeNoRequest()
    {
        // Arrange
        var cache = new DataListPageCache();
        var server = new FakeListServer(60);
        var state = State(1);
        await DataListLoader.LoadAsync(cache, Scope, ListId, state, PageSize, server.FetchAsync, CancellationToken.None);

        // Act
        var second = await DataListLoader.LoadAsync(cache, Scope, ListId, state, PageSize, server.FetchAsync, CancellationToken.None);

        // Assert
        server.Requests.Should().ContainSingle();
        second.Page!.Items.Should().HaveCount(25).And.StartWith("row-025");
        second.PageOffset.Should().Be(1);
    }

    [Fact]
    public async Task Load_WhenPageOffsetIsBeyondTheLastPage_ShouldLoadTheLastPageWithoutAnError()
    {
        // Arrange
        var cache = new DataListPageCache();
        var server = new FakeListServer(60);

        // Act
        var result = await DataListLoader.LoadAsync(cache, Scope, ListId, State(9), PageSize, server.FetchAsync, CancellationToken.None);

        // Assert
        result.ErrorMessage.Should().BeNull();
        result.PageOffset.Should().Be(2);
        result.Page!.Items.Should().HaveCount(10);
        server.Requests.Select(request => request.PageOffset).Should().Equal(9, 0, 2);
    }

    [Fact]
    public async Task Load_WhenResultIsEmptyAndOffsetIsStale_ShouldFallBackToTheEmptyFirstPageWithoutLooping()
    {
        // Arrange
        var cache = new DataListPageCache();
        var server = new FakeListServer(0);

        // Act
        var result = await DataListLoader.LoadAsync(cache, Scope, ListId, State(4), PageSize, server.FetchAsync, CancellationToken.None);

        // Assert
        result.ErrorMessage.Should().BeNull();
        result.PageOffset.Should().Be(0);
        result.Page!.Items.Should().BeEmpty();
        result.Page.TotalCount.Should().Be(0);
        server.Requests.Select(request => request.PageOffset).Should().Equal(4, 0);
    }

    [Fact]
    public async Task Load_WhenFirstPageIsEmpty_ShouldReturnItWithOneRequest()
    {
        // Arrange
        var server = new FakeListServer(0);

        // Act
        var result = await DataListLoader.LoadAsync(new DataListPageCache(), Scope, ListId, State(0), PageSize, server.FetchAsync, CancellationToken.None);

        // Assert
        result.Page!.Items.Should().BeEmpty();
        server.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Load_WhenBadRequestIsUnrelatedToTheOffset_ShouldReportTheFailure()
    {
        // Arrange
        var cache = new DataListPageCache();
        var server = new FakeListServer(60) { FailWithStatus = 400 };

        // Act
        var result = await DataListLoader.LoadAsync(cache, Scope, ListId, State(1), PageSize, server.FetchAsync, CancellationToken.None);

        // Assert
        result.Page.Should().BeNull();
        result.ErrorMessage.Should().Be("Validation failed.");
        server.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Load_WhenFirstPageSucceedsButRequestedOffsetIsValid_ShouldKeepTheOriginalFailure()
    {
        // Arrange
        var cache = new DataListPageCache();
        var server = new FakeListServer(60);
        var fetch = new DataListFetch<string>(async (request, cancellationToken) =>
            request.PageOffset == 1 ? DataListFetchResult<string>.Failure(400, "Search is too long.") : await server.FetchAsync(request, cancellationToken)
        );

        // Act
        var result = await DataListLoader.LoadAsync(cache, Scope, ListId, State(1), PageSize, fetch, CancellationToken.None);

        // Assert
        result.ErrorMessage.Should().Be("Search is too long.");
        result.PageOffset.Should().Be(1);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(401)]
    public async Task Load_WhenFetchFails_ShouldNotCacheTheFailure(int statusCode)
    {
        // Arrange
        var cache = new DataListPageCache();
        var server = new FakeListServer(60) { FailWithStatus = statusCode };
        await DataListLoader.LoadAsync(cache, Scope, ListId, State(0), PageSize, server.FetchAsync, CancellationToken.None);
        server.FailWithStatus = null;

        // Act
        var result = await DataListLoader.LoadAsync(cache, Scope, ListId, State(0), PageSize, server.FetchAsync, CancellationToken.None);

        // Assert
        result.Page.Should().NotBeNull();
        server.Requests.Should().HaveCount(2);
        cache.Count.Should().Be(1);
    }

    [Fact]
    public async Task Load_WhenCancelled_ShouldThrowAndCacheNothing()
    {
        // Arrange
        var cache = new DataListPageCache();
        var server = new FakeListServer(60);
        server.Hold(1);
        using var cancellation = new CancellationTokenSource();
        var load = DataListLoader.LoadAsync(cache, Scope, ListId, State(0), PageSize, server.FetchAsync, cancellation.Token);

        // Act
        await cancellation.CancelAsync();
        var act = () => load;

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        cache.Count.Should().Be(0);
    }

    [Fact]
    public async Task Load_WhenInvalidatedWhileInFlight_ShouldNotStoreTheOlderResult()
    {
        // Arrange
        var cache = new DataListPageCache();
        var server = new FakeListServer(60);
        server.Hold(1);
        var olderLoad = DataListLoader.LoadAsync(cache, Scope, ListId, State(0), PageSize, server.FetchAsync, CancellationToken.None);
        cache.Invalidate(ListId);
        server.RowCount = 59;
        var newer = await DataListLoader.LoadAsync(cache, Scope, ListId, State(0), PageSize, server.FetchAsync, CancellationToken.None);

        // Act
        server.Release(1);
        await olderLoad;
        var cached = await DataListLoader.LoadAsync(cache, Scope, ListId, State(0), PageSize, server.FetchAsync, CancellationToken.None);

        // Assert
        newer.Page!.TotalCount.Should().Be(59);
        cached.Page!.TotalCount.Should().Be(59);
        server.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(0)]
    public async Task Load_WhenCachedFirstPageIsStaleAndOffsetIsNowBeyondTheResult_ShouldRecoverToTheFreshFirstPage(int rowsAfterShrink)
    {
        // Arrange
        var cache = new DataListPageCache();
        var server = new FakeListServer(60);
        await DataListLoader.LoadAsync(cache, Scope, ListId, State(0), PageSize, server.FetchAsync, CancellationToken.None);
        server.RowCount = rowsAfterShrink;

        // Act
        var result = await DataListLoader.LoadAsync(cache, Scope, ListId, State(2), PageSize, server.FetchAsync, CancellationToken.None);
        var cachedFirst = await DataListLoader.LoadAsync(cache, Scope, ListId, State(0), PageSize, server.FetchAsync, CancellationToken.None);

        // Assert
        result.ErrorMessage.Should().BeNull();
        result.PageOffset.Should().Be(0);
        result.Page!.TotalCount.Should().Be(rowsAfterShrink);
        result.Page.Items.Should().HaveCount(rowsAfterShrink);
        cachedFirst.Page!.TotalCount.Should().Be(rowsAfterShrink);
        server.Requests.Select(request => request.PageOffset).Should().Equal(0, 2, 0);
    }

    [Fact]
    public async Task Load_WhenFallbackPageWasCachedBeforeTheServerShrank_ShouldRecoverToTheFreshLastPage()
    {
        // Arrange
        var cache = new DataListPageCache();
        var server = new FakeListServer(60);
        await DataListLoader.LoadAsync(cache, Scope, ListId, State(0), PageSize, server.FetchAsync, CancellationToken.None);
        await DataListLoader.LoadAsync(cache, Scope, ListId, State(1), PageSize, server.FetchAsync, CancellationToken.None);
        server.RowCount = 30;

        // Act
        var result = await DataListLoader.LoadAsync(cache, Scope, ListId, State(2), PageSize, server.FetchAsync, CancellationToken.None);
        var cachedLast = await DataListLoader.LoadAsync(cache, Scope, ListId, State(1), PageSize, server.FetchAsync, CancellationToken.None);

        // Assert
        result.ErrorMessage.Should().BeNull();
        result.PageOffset.Should().Be(1);
        result.Page!.TotalCount.Should().Be(30);
        result.Page.Items.Should().Equal("row-025", "row-026", "row-027", "row-028", "row-029");
        cachedLast.Page.Should().BeSameAs(result.Page);
        server.Requests.Select(request => request.PageOffset).Should().Equal(0, 1, 2, 0, 1);
    }

    [Fact]
    public async Task Load_WhenCachedFirstPageIsStaleButTheOffsetIsStillValid_ShouldKeepTheUnrelatedFailure()
    {
        // Arrange
        var cache = new DataListPageCache();
        var server = new FakeListServer(20);
        await DataListLoader.LoadAsync(cache, Scope, ListId, State(0), PageSize, server.FetchAsync, CancellationToken.None);
        server.RowCount = 60;
        var fetch = new DataListFetch<string>(async (request, cancellationToken) =>
            request.PageOffset == 1 ? DataListFetchResult<string>.Failure(400, "Search is too long.") : await server.FetchAsync(request, cancellationToken)
        );

        // Act
        var result = await DataListLoader.LoadAsync(cache, Scope, ListId, State(1), PageSize, fetch, CancellationToken.None);

        // Assert
        result.Page.Should().BeNull();
        result.ErrorMessage.Should().Be("Search is too long.");
        result.PageOffset.Should().Be(1);
        server.Requests.Select(request => request.PageOffset).Should().Equal(0, 0);
    }

    [Fact]
    public async Task Load_WhenCancelledDuringRecovery_ShouldThrowAndStoreNothing()
    {
        // Arrange
        var cache = new DataListPageCache();
        var server = new FakeListServer(60);
        await DataListLoader.LoadAsync(cache, Scope, ListId, State(0), PageSize, server.FetchAsync, CancellationToken.None);
        server.RowCount = 20;
        var cancellation = new CancellationTokenSource();
        var fetch = new DataListFetch<string>(async (request, cancellationToken) =>
            {
                var result = await server.FetchAsync(request, cancellationToken);
                if (server.Requests.Count == 3) await cancellation.CancelAsync();
                return result;
            }
        );

        // Act
        var act = () => DataListLoader.LoadAsync(cache, Scope, ListId, State(2), PageSize, fetch, cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        server.Requests.Select(request => request.PageOffset).Should().Equal(0, 2, 0);
        cache.Count.Should().Be(1);
        cache.TryGet<string>(Scope, ListId, State(0).QueryKey, PageSize, 0, out var cachedFirst).Should().BeTrue();
        cachedFirst.TotalCount.Should().Be(60);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Load_WhenInvalidatedDuringRecovery_ShouldNotStoreTheOlderRecoveryPages(int heldRequest)
    {
        // Arrange
        var cache = new DataListPageCache();
        var server = new FakeListServer(60);
        await DataListLoader.LoadAsync(cache, Scope, ListId, State(0), PageSize, server.FetchAsync, CancellationToken.None);
        await DataListLoader.LoadAsync(cache, Scope, ListId, State(1), PageSize, server.FetchAsync, CancellationToken.None);
        server.RowCount = 30;
        server.Hold(heldRequest);
        var olderLoad = DataListLoader.LoadAsync(cache, Scope, ListId, State(2), PageSize, server.FetchAsync, CancellationToken.None);
        cache.Invalidate(ListId);
        server.RowCount = 10;

        // Act
        server.Release(heldRequest);
        await olderLoad;
        var cachedPagesAfterOlderLoad = cache.Count;
        var newer = await DataListLoader.LoadAsync(cache, Scope, ListId, State(0), PageSize, server.FetchAsync, CancellationToken.None);

        // Assert
        cachedPagesAfterOlderLoad.Should().Be(0);
        newer.Page!.TotalCount.Should().Be(10);
        server.Requests.Should().HaveCount(6);
    }

    private static DataListState State(int pageOffset)
    {
        return new DataListState("Name", SortOrder.Ascending, pageOffset, null);
    }
}
