// Spike code (Blazor edition, stage B3): the users list state that the React edition keeps in its route search params
// (search, userRole, userStatus, startDate, endDate, orderBy, sortOrder, pageOffset, userId), plus the list mode that
// this spike compares. Parsed from and written back to the URL.

using System.Globalization;
using Account.Features.Users.Requests;

namespace Blazor.Client.Users;

public enum UsersListMode
{
    Virtual,
    Paged
}

public sealed record UsersListState(
    string? Search = null,
    UserRole? UserRole = null,
    UserStatus? UserStatus = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    SortableUserProperties OrderBy = SortableUserProperties.Name,
    SortOrder SortOrder = SortOrder.Ascending,
    int PageOffset = 0,
    string? UserId = null,
    UsersListMode Mode = UsersListMode.Virtual
)
{
    public int ActiveFilterCount => (UserRole is null ? 0 : 1) + (UserStatus is null ? 0 : 1) + (StartDate is not null && EndDate is not null ? 1 : 0);

    public bool HasFilters => !string.IsNullOrEmpty(Search) || ActiveFilterCount > 0;

    public static UsersListState FromUri(string uri)
    {
        var query = ParseQuery(new Uri(uri).Query);

        string? Value(string key)
        {
            return query.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;
        }

        TEnum? EnumValue<TEnum>(string key) where TEnum : struct
        {
            return Enum.TryParse<TEnum>(Value(key), true, out var parsed) ? parsed : null;
        }

        DateOnly? DateValue(string key)
        {
            return DateOnly.TryParse(Value(key), CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        }

        return new UsersListState(
            Value("search"),
            EnumValue<UserRole>("userRole"),
            EnumValue<UserStatus>("userStatus"),
            DateValue("startDate"),
            DateValue("endDate"),
            EnumValue<SortableUserProperties>("orderBy") ?? SortableUserProperties.Name,
            EnumValue<SortOrder>("sortOrder") ?? SortOrder.Ascending,
            int.TryParse(Value("pageOffset"), out var pageOffset) && pageOffset > 0 ? pageOffset : 0,
            Value("userId"),
            EnumValue<UsersListMode>("mode") ?? UsersListMode.Virtual
        );
    }

    // Defaults are left out of the URL, as the React edition does
    public string ToUri(string currentUri)
    {
        var path = new Uri(currentUri).GetLeftPart(UriPartial.Path);
        var parameters = new Dictionary<string, string?>
        {
            ["mode"] = Mode == UsersListMode.Virtual ? null : "paged",
            ["search"] = Search,
            ["userRole"] = UserRole?.ToString(),
            ["userStatus"] = UserStatus?.ToString(),
            ["startDate"] = StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["endDate"] = EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["orderBy"] = OrderBy == SortableUserProperties.Name ? null : OrderBy.ToString(),
            ["sortOrder"] = SortOrder == SortOrder.Ascending ? null : SortOrder.ToString(),
            ["pageOffset"] = Mode == UsersListMode.Paged && PageOffset > 0 ? PageOffset.ToString(CultureInfo.InvariantCulture) : null,
            ["userId"] = UserId
        };
        var query = FormatQuery(parameters);
        return query.Length == 0 ? path : $"{path}?{query}";
    }

    // The account API's users query; the filter part only, paging is set per request. A calendar date is sent as the date
    // itself, so it is carried at midnight UTC.
    public GetUsersQuery ToUsersQuery()
    {
        return new GetUsersQuery(Search, UserRole, UserStatus, ToDateTimeOffset(StartDate), ToDateTimeOffset(EndDate), OrderBy, SortOrder);
    }

    private static DateTimeOffset? ToDateTimeOffset(DateOnly? date)
    {
        return date is { } value ? new DateTimeOffset(value, TimeOnly.MinValue, TimeSpan.Zero) : null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .GroupBy(pair => Uri.UnescapeDataString(pair[0]), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => Uri.UnescapeDataString((group.Last().ElementAtOrDefault(1) ?? "").Replace('+', ' ')), StringComparer.OrdinalIgnoreCase);
    }

    private static string FormatQuery(Dictionary<string, string?> parameters)
    {
        return string.Join('&', parameters.Where(parameter => parameter.Value is not null).Select(parameter => $"{parameter.Key}={Uri.EscapeDataString(parameter.Value!)}"));
    }
}
