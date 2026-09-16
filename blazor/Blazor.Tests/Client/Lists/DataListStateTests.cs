using Blazor.Client.Components.Lists;
using FluentAssertions;
using SharedKernel.Persistence;

namespace Blazor.Tests.Client.Lists;

public sealed class DataListStateTests
{
    private const string Page = "https://app.dev.localhost:9000/blazor/app/users/quick";

    private static readonly DataListUrlOptions Options = new("Name", ["Name", "Email", "CreatedAt"], ["search", "userRole"], "userId");

    [Fact]
    public void ToUri_WhenStateHasDefaults_ShouldOmitEveryListParameter()
    {
        // Arrange
        var state = DataListState.Parse(Page, Options);

        // Act
        var uri = state.ToUri(Page, Options);

        // Assert
        uri.Should().Be(Page);
        state.Should().Be(new DataListState("Name", SortOrder.Ascending, 0, null));
    }

    [Fact]
    public void Parse_WhenUriWasWrittenFromState_ShouldRoundTrip()
    {
        // Arrange
        var state = new DataListState("CreatedAt", SortOrder.Descending, 3, "usr_01", new Dictionary<string, string> { ["search"] = "a b&c=d", ["userRole"] = "Admin" });

        // Act
        var uri = state.ToUri(Page, Options);
        var parsed = DataListState.Parse(uri, Options);

        // Assert
        uri.Should().Be($"{Page}?search=a%20b%26c%3Dd&userRole=Admin&orderBy=CreatedAt&sortOrder=Descending&pageOffset=3&userId=usr_01");
        parsed.Should().Be(state);
    }

    [Fact]
    public void ToUri_WhenUriHasUnrelatedAndQuickGridParameters_ShouldKeepUnrelatedAndDropQuickGrids()
    {
        // Arrange
        var current = $"{Page}?tab=members&sort=Email&direction=desc&page=4&pageOffset=2&keep=%C3%A6#rows";
        var state = DataListState.Parse(current, Options).WithPage(5);

        // Act
        var uri = state.ToUri(current, Options);

        // Assert
        uri.Should().Be($"{Page}?tab=members&keep=%C3%A6&pageOffset=5#rows");
    }

    [Theory]
    [InlineData("?orderBy=Unknown&sortOrder=sideways&pageOffset=-2")]
    [InlineData("?orderBy=&sortOrder=1&pageOffset=abc")]
    [InlineData("?orderBy=Name%&pageOffset=99999999999999999999")]
    [InlineData("?pageOffset=%2B3&sortOrder=")]
    public void Parse_WhenValuesAreMalformed_ShouldFallBackToDefaultsWithoutThrowing(string query)
    {
        // Act
        var state = DataListState.Parse($"{Page}{query}", Options);

        // Assert
        state.OrderBy.Should().Be("Name");
        state.SortOrder.Should().Be(SortOrder.Ascending);
        state.PageOffset.Should().Be(0);
    }

    [Fact]
    public void Parse_WhenNamesDifferInCase_ShouldReadThemAndCanonicalizeTheSortKey()
    {
        // Act
        var state = DataListState.Parse($"{Page}?ORDERBY=email&SortOrder=DESCENDING&PageOffset=2&SEARCH=ann", Options);

        // Assert
        state.Should().Be(new DataListState("Email", SortOrder.Descending, 2, null, new Dictionary<string, string> { ["search"] = "ann" }));
    }

    [Fact]
    public void Parse_WhenPageNormalizesFilters_ShouldApplyTheNormalizer()
    {
        // Arrange
        var options = new DataListUrlOptions("Name", ["Name"], ["search", "userRole"], normalizeFilters: filters => filters.Where(filter => filter.Key == "search").ToDictionary());

        // Act
        var state = DataListState.Parse($"{Page}?search=ann&userRole=garbage", options);

        // Assert
        state.Filters.Should().BeEquivalentTo(new Dictionary<string, string> { ["search"] = "ann" });
    }

    [Fact]
    public void WithSort_ShouldResetThePageAndKeepFiltersAndSelectedKey()
    {
        // Arrange
        var state = new DataListState("Name", SortOrder.Ascending, 4, "usr_01", new Dictionary<string, string> { ["search"] = "ann" });

        // Act
        var sorted = state.WithSort("Email", SortOrder.Descending);

        // Assert
        sorted.Should().Be(new DataListState("Email", SortOrder.Descending, 0, "usr_01", new Dictionary<string, string> { ["search"] = "ann" }));
    }

    [Fact]
    public void WithFilters_ShouldResetThePageAndRemoveEmptyValues()
    {
        // Arrange
        var state = new DataListState("Email", SortOrder.Descending, 4, null, new Dictionary<string, string> { ["search"] = "ann", ["userRole"] = "Admin" });

        // Act
        var filtered = state.WithFilters(new Dictionary<string, string?> { ["search"] = "bo", ["userRole"] = "" });

        // Assert
        filtered.Should().Be(new DataListState("Email", SortOrder.Descending, 0, null, new Dictionary<string, string> { ["search"] = "bo" }));
        filtered.QueryKey.Should().NotBe(state.QueryKey);
    }

    [Fact]
    public void WithPageAndSelectedKey_ShouldNotChangeTheQueryKey()
    {
        // Arrange
        var state = new DataListState("Email", SortOrder.Descending, 0, null, new Dictionary<string, string> { ["search"] = "ann" });

        // Act
        var changed = state.WithPage(3).WithSelectedKey("usr_02");

        // Assert
        changed.QueryKey.Should().Be(state.QueryKey);
        changed.Should().NotBe(state);
    }

    [Fact]
    public void ToUri_WhenPrefixIsSet_ShouldWriteOnlyPrefixedListParameters()
    {
        // Arrange
        var first = new DataListUrlOptions("Name", ["Name", "Email"]);
        var second = new DataListUrlOptions("Name", ["Name", "Email"], parameterPrefix: "second");
        var current = $"{Page}?orderBy=Email";

        // Act
        var uri = DataListState.Parse(current, second).WithSort("Email", SortOrder.Descending).WithPage(1).ToUri(current, second);

        // Assert
        uri.Should().Be($"{Page}?orderBy=Email&secondOrderBy=Email&secondSortOrder=Descending&secondPageOffset=1");
        DataListState.Parse(uri, first).Should().Be(new DataListState("Email", SortOrder.Ascending, 0, null));
    }
}
