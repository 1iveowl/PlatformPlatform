using Blazor.Client.Components.Lists;
using FluentAssertions;

namespace Blazor.Tests.Client.Lists;

public sealed class DataListPageCacheTests
{
    private const string Alice = "tnt_1/usr_alice";
    private const string Bob = "tnt_1/usr_bob";
    private const string Users = "users";
    private static readonly DataListPage<string> PageOne = new(["one"], 30);

    [Fact]
    public void TryGet_WhenAnyKeyPartDiffers_ShouldMiss()
    {
        // Arrange
        var cache = new DataListPageCache();
        cache.Store(cache.BeginFetch(Alice, Users), Users, "search=ann&orderBy=Name&sortOrder=Ascending", 25, 1, PageOne);

        // Act
        var hit = cache.TryGet<string>(Alice, Users, "search=ann&orderBy=Name&sortOrder=Ascending", 25, 1, out var page);
        var otherFilter = cache.TryGet<string>(Alice, Users, "search=bo&orderBy=Name&sortOrder=Ascending", 25, 1, out _);
        var otherSort = cache.TryGet<string>(Alice, Users, "search=ann&orderBy=Name&sortOrder=Descending", 25, 1, out _);
        var otherPageSize = cache.TryGet<string>(Alice, Users, "search=ann&orderBy=Name&sortOrder=Ascending", 100, 1, out _);
        var otherOffset = cache.TryGet<string>(Alice, Users, "search=ann&orderBy=Name&sortOrder=Ascending", 25, 2, out _);
        var otherList = cache.TryGet<string>(Alice, "invitations", "search=ann&orderBy=Name&sortOrder=Ascending", 25, 1, out _);

        // Assert
        hit.Should().BeTrue();
        page.Should().BeSameAs(PageOne);
        new[] { otherFilter, otherSort, otherPageSize, otherOffset, otherList }.Should().AllBeEquivalentTo(false);
    }

    [Fact]
    public void Invalidate_ShouldDropEveryFilterAndSortVariantOfTheListOnly()
    {
        // Arrange
        var cache = new DataListPageCache();
        cache.Store(cache.BeginFetch(Alice, Users), Users, "orderBy=Name&sortOrder=Ascending", 25, 0, PageOne);
        cache.Store(cache.BeginFetch(Alice, Users), Users, "search=ann&orderBy=Email&sortOrder=Descending", 25, 3, PageOne);
        cache.Store(cache.BeginFetch(Alice, "invitations"), "invitations", "orderBy=Name&sortOrder=Ascending", 25, 0, PageOne);

        // Act
        cache.Invalidate(Users);

        // Assert
        cache.TryGet<string>(Alice, Users, "orderBy=Name&sortOrder=Ascending", 25, 0, out _).Should().BeFalse();
        cache.TryGet<string>(Alice, Users, "search=ann&orderBy=Email&sortOrder=Descending", 25, 3, out _).Should().BeFalse();
        cache.TryGet<string>(Alice, "invitations", "orderBy=Name&sortOrder=Ascending", 25, 0, out _).Should().BeTrue();
    }

    [Fact]
    public void Store_WhenFetchStartedBeforeAnInvalidation_ShouldNotRepopulateTheCache()
    {
        // Arrange
        var cache = new DataListPageCache();
        var staleToken = cache.BeginFetch(Alice, Users);
        cache.Invalidate(Users);

        // Act
        var stored = cache.Store(staleToken, Users, "orderBy=Name&sortOrder=Ascending", 25, 0, PageOne);

        // Assert
        stored.Should().BeFalse();
        cache.Count.Should().Be(0);
        cache.Store(cache.BeginFetch(Alice, Users), Users, "orderBy=Name&sortOrder=Ascending", 25, 0, PageOne).Should().BeTrue();
    }

    [Fact]
    public void Scope_WhenIdentityChanges_ShouldClearAndKeepTheIdentitiesApart()
    {
        // Arrange
        var cache = new DataListPageCache();
        var aliceToken = cache.BeginFetch(Alice, Users);
        cache.Store(aliceToken, Users, "orderBy=Name&sortOrder=Ascending", 25, 0, PageOne);

        // Act
        var bobHit = cache.TryGet<string>(Bob, Users, "orderBy=Name&sortOrder=Ascending", 25, 0, out _);
        var lateAliceStore = cache.Store(aliceToken, Users, "orderBy=Name&sortOrder=Ascending", 25, 1, PageOne);
        var aliceHitAfterBob = cache.TryGet<string>(Alice, Users, "orderBy=Name&sortOrder=Ascending", 25, 0, out _);

        // Assert
        bobHit.Should().BeFalse();
        lateAliceStore.Should().BeFalse();
        aliceHitAfterBob.Should().BeFalse();
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void Store_WhenFull_ShouldEvictTheLeastRecentlyUsedPage()
    {
        // Arrange
        var cache = new DataListPageCache();
        for (var offset = 0; offset < DataListPageCache.MaxEntries; offset++)
        {
            cache.Store(cache.BeginFetch(Alice, Users), Users, "orderBy=Name&sortOrder=Ascending", 25, offset, PageOne);
        }

        cache.TryGet<string>(Alice, Users, "orderBy=Name&sortOrder=Ascending", 25, 0, out _);

        // Act
        cache.Store(cache.BeginFetch(Alice, Users), Users, "orderBy=Name&sortOrder=Ascending", 25, DataListPageCache.MaxEntries, PageOne);

        // Assert
        cache.Count.Should().Be(DataListPageCache.MaxEntries);
        cache.TryGet<string>(Alice, Users, "orderBy=Name&sortOrder=Ascending", 25, 0, out _).Should().BeTrue();
        cache.TryGet<string>(Alice, Users, "orderBy=Name&sortOrder=Ascending", 25, 1, out _).Should().BeFalse();
    }
}
