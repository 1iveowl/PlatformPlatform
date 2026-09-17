// The users page's role, status and modified-date filters, the React edition's useUserFilters: which filters count towards
// the badge, the URL changes that set or clear them, and whether they show inline in the toolbar or behind the filter
// button. Filters show inline only while the toolbar is at least InlineThresholdRem wide; below that the button opens the
// filter dialog. The URL stays the only store of the filter values; this class decides, UsersSurface applies the changes
// through DataList.SetFiltersAsync.

using System.Globalization;

namespace Blazor.Client.Users;

public enum UsersFilterButtonAction
{
    ClearFilters,
    ShowInline,
    OpenDialog
}

public sealed record UsersDateRange(string Start, string End);

public sealed class UsersFilterModel
{
    public const int InlineThresholdRem = 54;
    private const string DateFormat = "yyyy-MM-dd";

    private bool _isMeasured;

    public bool IsExpanded { get; private set; }

    public bool IsWide { get; private set; }

    public bool IsDialogOpen { get; set; }

    public string ButtonLabel => IsExpanded ? UsersStrings.ClearFilters : UsersStrings.ShowSearchFilters;

    // Role, status and a complete date range count; search does not
    public static int ActiveCount(IReadOnlyDictionary<string, string> filters)
    {
        var count = 0;
        if (filters.ContainsKey(UsersListSource.UserRoleParameter)) count++;
        if (filters.ContainsKey(UsersListSource.UserStatusParameter)) count++;
        if (filters.ContainsKey(UsersListSource.StartDateParameter) && filters.ContainsKey(UsersListSource.EndDateParameter)) count++;
        return count;
    }

    // The dialog's Clear is enabled while a filter or a search is active
    public static bool CanClear(IReadOnlyDictionary<string, string> filters)
    {
        return ActiveCount(filters) > 0 || filters.ContainsKey(UsersListSource.SearchParameter);
    }

    public static IReadOnlyDictionary<string, string?> ClearChanges()
    {
        return UsersListSource.FilterParameters.ToDictionary(name => name, _ => (string?)null);
    }

    // An empty value is "Any role"; anything that is not a role name clears the filter
    public static IReadOnlyDictionary<string, string?> RoleChanges(string? value)
    {
        var role = Enum.GetNames<UserRole>().FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
        return new Dictionary<string, string?> { [UsersListSource.UserRoleParameter] = role };
    }

    public static IReadOnlyDictionary<string, string?> StatusChanges(string? value)
    {
        var status = Enum.GetNames<UserStatus>().FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
        return new Dictionary<string, string?> { [UsersListSource.UserStatusParameter] = status };
    }

    // The two native date inputs apply as one range, as the React range picker does: a complete range with the start on or
    // before the end is written, anything else (one bound, an inverted range, both empty) removes the range from the URL
    public static IReadOnlyDictionary<string, string?> DateRangeChanges(string? start, string? end)
    {
        var startDate = ParseDate(start);
        var endDate = ParseDate(end);
        var isComplete = startDate is not null && endDate is not null && startDate <= endDate;
        return new Dictionary<string, string?>
        {
            [UsersListSource.StartDateParameter] = isComplete ? FormatDate(startDate!.Value) : null,
            [UsersListSource.EndDateParameter] = isComplete ? FormatDate(endDate!.Value) : null
        };
    }

    public static UsersDateRange DateRange(IReadOnlyDictionary<string, string> filters)
    {
        return new UsersDateRange(filters.GetValueOrDefault(UsersListSource.StartDateParameter) ?? "", filters.GetValueOrDefault(UsersListSource.EndDateParameter) ?? "");
    }

    // Whether applying the changes would change the URL's filter values
    public static bool Changes(IReadOnlyDictionary<string, string> filters, IReadOnlyDictionary<string, string?> changes)
    {
        return changes.Any(change => filters.GetValueOrDefault(change.Key) != change.Value);
    }

    // The toolbar was measured: wide with active filters expands them, narrow collapses them. Returns whether anything changed.
    public bool WidthChanged(bool isWide, IReadOnlyDictionary<string, string> filters)
    {
        _isMeasured = true;
        var wasWide = IsWide;
        IsWide = isWide;
        return Reconcile(filters) || wasWide != isWide;
    }

    // Filters changed through the URL (a filter control, Back or Forward); active filters on a wide toolbar show inline
    public bool FiltersChanged(IReadOnlyDictionary<string, string> filters)
    {
        return _isMeasured && Reconcile(filters);
    }

    public UsersFilterButtonAction ButtonClicked()
    {
        if (IsExpanded)
        {
            Cleared();
            return UsersFilterButtonAction.ClearFilters;
        }

        if (IsWide)
        {
            IsExpanded = true;
            return UsersFilterButtonAction.ShowInline;
        }

        IsDialogOpen = true;
        return UsersFilterButtonAction.OpenDialog;
    }

    // Clearing all filters collapses the inline filters and closes the dialog
    public void Cleared()
    {
        IsExpanded = false;
        IsDialogOpen = false;
    }

    public bool ShowsBadge(IReadOnlyDictionary<string, string> filters)
    {
        return !IsExpanded && ActiveCount(filters) > 0;
    }

    private bool Reconcile(IReadOnlyDictionary<string, string> filters)
    {
        if (IsWide && !IsExpanded && ActiveCount(filters) > 0)
        {
            IsExpanded = true;
            IsDialogOpen = false;
            return true;
        }

        if (!IsWide && IsExpanded)
        {
            IsExpanded = false;
            return true;
        }

        return false;
    }

    private static DateOnly? ParseDate(string? value)
    {
        return DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    private static string FormatDate(DateOnly date)
    {
        return date.ToString(DateFormat, CultureInfo.InvariantCulture);
    }
}
