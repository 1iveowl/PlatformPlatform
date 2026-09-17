using Blazor.Client.Components.Lists;
using Blazor.Client.Users;
using FluentAssertions;
using SharedKernel.Localization;

namespace Blazor.Tests.Client.Users;

// The users filters count, clear and round-trip through the URL as the React edition's useUserFilters does, and switch
// between inline filters and the dialog by the toolbar width
public sealed class UsersFilterModelTests
{
    private const string Page = "https://app.dev.localhost:9000/blazor/account/users";

    private static readonly DataListUrlOptions Options = new(UsersListSource.DefaultOrderBy, ["Name", "Email"], UsersListSource.FilterParameters, UsersListSource.SelectedKeyParameter,
        normalizeFilters: UsersListSource.NormalizeFilters
    );

    [Theory]
    [InlineData("", 0)]
    [InlineData("?search=ann", 0)]
    [InlineData("?userRole=Admin", 1)]
    [InlineData("?userRole=Admin&userStatus=Pending", 2)]
    [InlineData("?startDate=2026-01-01", 0)]
    [InlineData("?startDate=2026-01-01&endDate=2026-01-31", 1)]
    [InlineData("?userRole=Owner&userStatus=Active&startDate=2026-01-01&endDate=2026-01-31&search=ann", 3)]
    [InlineData("?userRole=Garbage&userStatus=7&startDate=31-01-2026&endDate=2026-01-31", 0)]
    public void ActiveCount_WhenFiltersParsedFromTheUrl_ShouldCountRoleStatusAndACompleteDateRange(string query, int expected)
    {
        // Arrange
        var filters = DataListState.Parse($"{Page}{query}", Options).Filters;

        // Act
        var count = UsersFilterModel.ActiveCount(filters);

        // Assert
        count.Should().Be(expected);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("?search=ann", true)]
    [InlineData("?userStatus=Pending", true)]
    public void CanClear_WhenFiltersGiven_ShouldBeEnabledForAnyFilterOrSearch(string query, bool expected)
    {
        // Arrange
        var filters = DataListState.Parse($"{Page}{query}", Options).Filters;

        // Act
        var canClear = UsersFilterModel.CanClear(filters);

        // Assert
        canClear.Should().Be(expected);
    }

    [Fact]
    public void ClearChanges_WhenAppliedToTheUrl_ShouldRemoveSearchAndEveryFilterAndKeepTheSort()
    {
        // Arrange
        var state = DataListState.Parse($"{Page}?search=ann&userRole=Admin&userStatus=Pending&startDate=2026-01-01&endDate=2026-01-31&orderBy=Email", Options);

        // Act
        var cleared = state.WithFilters(UsersFilterModel.ClearChanges());

        // Assert
        cleared.Filters.Should().BeEmpty();
        cleared.ToUri(Page, Options).Should().Be($"{Page}?orderBy=Email");
    }

    [Fact]
    public void FilterChanges_WhenAppliedToTheUrl_ShouldRoundTripThroughParse()
    {
        // Arrange
        var state = DataListState.Parse(Page, Options);

        // Act
        var filtered = state.WithFilters(UsersFilterModel.RoleChanges("admin"))
            .WithFilters(UsersFilterModel.StatusChanges("Pending"))
            .WithFilters(UsersFilterModel.DateRangeChanges("2026-01-01", "2026-01-31"));
        var uri = filtered.ToUri(Page, Options);
        var parsed = DataListState.Parse(uri, Options);

        // Assert
        uri.Should().Be($"{Page}?userRole=Admin&userStatus=Pending&startDate=2026-01-01&endDate=2026-01-31");
        parsed.Filters.Should().BeEquivalentTo(filtered.Filters);
        UsersFilterModel.ActiveCount(parsed.Filters).Should().Be(3);
    }

    [Theory]
    [InlineData("", "Any role")]
    [InlineData("Superuser", "an unknown role")]
    public void RoleChanges_WhenValueIsNotARole_ShouldClearTheRole(string value, string because)
    {
        // Act
        var changes = UsersFilterModel.RoleChanges(value);

        // Assert
        changes.Should().BeEquivalentTo(new Dictionary<string, string?> { [UsersListSource.UserRoleParameter] = null }, because);
    }

    [Theory]
    [InlineData("2026-01-01", "2026-01-31", "2026-01-01", "2026-01-31")]
    [InlineData("2026-01-31", "2026-01-31", "2026-01-31", "2026-01-31")]
    [InlineData("2026-01-01", "", null, null)]
    [InlineData("", "2026-01-31", null, null)]
    [InlineData("2026-02-01", "2026-01-31", null, null)]
    [InlineData("", "", null, null)]
    public void DateRangeChanges_WhenBoundsGiven_ShouldWriteOnlyACompleteRange(string start, string end, string? expectedStart, string? expectedEnd)
    {
        // Act
        var changes = UsersFilterModel.DateRangeChanges(start, end);

        // Assert
        changes.Should().BeEquivalentTo(new Dictionary<string, string?> { [UsersListSource.StartDateParameter] = expectedStart, [UsersListSource.EndDateParameter] = expectedEnd });
    }

    [Fact]
    public void Changes_WhenTheUrlAlreadyHasTheValues_ShouldReportNoChange()
    {
        // Arrange
        var filters = new Dictionary<string, string> { [UsersListSource.UserRoleParameter] = "Admin" };

        // Act
        var same = UsersFilterModel.Changes(filters, UsersFilterModel.RoleChanges("Admin"));
        var partialRangeOnNoRange = UsersFilterModel.Changes(filters, UsersFilterModel.DateRangeChanges("2026-01-01", ""));
        var different = UsersFilterModel.Changes(filters, UsersFilterModel.RoleChanges("Owner"));

        // Assert
        same.Should().BeFalse();
        partialRangeOnNoRange.Should().BeFalse();
        different.Should().BeTrue();
    }

    [Fact]
    public void WidthChanged_WhenWideWithActiveFilters_ShouldExpandAndHideTheBadge()
    {
        // Arrange
        var model = new UsersFilterModel();
        var filters = new Dictionary<string, string> { [UsersListSource.UserRoleParameter] = "Admin" };

        // Act
        var changed = model.WidthChanged(true, filters);

        // Assert
        changed.Should().BeTrue();
        model.IsExpanded.Should().BeTrue();
        model.ShowsBadge(filters).Should().BeFalse();
        model.ButtonLabel.Should().Be(UsersStrings.ClearFilters);
    }

    [Fact]
    public void WidthChanged_WhenNarrowWhileExpanded_ShouldCollapseAndShowTheBadge()
    {
        // Arrange
        var model = new UsersFilterModel();
        var filters = new Dictionary<string, string> { [UsersListSource.UserRoleParameter] = "Admin", [UsersListSource.UserStatusParameter] = "Active" };
        model.WidthChanged(true, filters);

        // Act
        var changed = model.WidthChanged(false, filters);

        // Assert
        changed.Should().BeTrue();
        model.IsExpanded.Should().BeFalse();
        model.ShowsBadge(filters).Should().BeTrue();
        model.ButtonLabel.Should().Be(UsersStrings.ShowSearchFilters);
    }

    [Fact]
    public void FiltersChanged_BeforeTheToolbarWasMeasured_ShouldStayCollapsed()
    {
        // Arrange
        var model = new UsersFilterModel();

        // Act
        var changed = model.FiltersChanged(new Dictionary<string, string> { [UsersListSource.UserRoleParameter] = "Admin" });

        // Assert
        changed.Should().BeFalse();
        model.IsExpanded.Should().BeFalse();
    }

    [Fact]
    public void ButtonClicked_WhenCollapsedOnAWideToolbar_ShouldShowFiltersInline()
    {
        // Arrange
        var model = new UsersFilterModel();
        model.WidthChanged(true, new Dictionary<string, string>());

        // Act
        var action = model.ButtonClicked();

        // Assert
        action.Should().Be(UsersFilterButtonAction.ShowInline);
        model.IsExpanded.Should().BeTrue();
        model.IsDialogOpen.Should().BeFalse();
    }

    [Fact]
    public void ButtonClicked_WhenCollapsedOnANarrowToolbar_ShouldOpenTheDialog()
    {
        // Arrange
        var model = new UsersFilterModel();
        model.WidthChanged(false, new Dictionary<string, string>());

        // Act
        var action = model.ButtonClicked();

        // Assert
        action.Should().Be(UsersFilterButtonAction.OpenDialog);
        model.IsDialogOpen.Should().BeTrue();
        model.IsExpanded.Should().BeFalse();
    }

    [Fact]
    public void ButtonClicked_WhenExpanded_ShouldClearAndCollapse()
    {
        // Arrange
        var model = new UsersFilterModel();
        model.WidthChanged(true, new Dictionary<string, string> { [UsersListSource.UserStatusParameter] = "Pending" });

        // Act
        var action = model.ButtonClicked();

        // Assert
        action.Should().Be(UsersFilterButtonAction.ClearFilters);
        model.IsExpanded.Should().BeFalse();
    }

    [Fact]
    public void Cleared_WhenTheDialogIsOpen_ShouldCloseIt()
    {
        // Arrange
        var model = new UsersFilterModel();
        model.WidthChanged(false, new Dictionary<string, string>());
        model.ButtonClicked();

        // Act
        model.Cleared();

        // Assert
        model.IsDialogOpen.Should().BeFalse();
        model.IsExpanded.Should().BeFalse();
    }
}
