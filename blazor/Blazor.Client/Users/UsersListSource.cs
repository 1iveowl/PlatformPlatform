// The users list's side of DataList: its URL filter parameters, which match the React edition's route search parameters,
// their normalization, the sort keys, and the fetch through the typed users client. Malformed filter values are dropped
// rather than sent; the API validates the rest.

using System.Globalization;
using Account.Client;
using Account.Features.Authentication.Queries;
using Account.Features.Users.Requests;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;

namespace Blazor.Client.Users;

public static class UsersListSource
{
    public const string ListId = "account-users";
    public const string SelectedKeyParameter = "userId";
    public const string SearchParameter = "search";
    public const string UserRoleParameter = "userRole";
    public const string UserStatusParameter = "userStatus";
    public const string StartDateParameter = "startDate";
    public const string EndDateParameter = "endDate";
    public const string DefaultOrderBy = nameof(SortableUserProperties.Name);
    private const string DateFormat = "yyyy-MM-dd";

    public static readonly IReadOnlyList<string> FilterParameters = [SearchParameter, UserRoleParameter, UserStatusParameter, StartDateParameter, EndDateParameter];

    // The identity the cached pages belong to
    public static string CacheScope(BootstrapResponse bootstrap)
    {
        return $"{bootstrap.User?.TenantId}/{bootstrap.User?.Id}";
    }

    public static IReadOnlyDictionary<string, string> NormalizeFilters(IReadOnlyDictionary<string, string> filters)
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        if (filters.TryGetValue(SearchParameter, out var search) && !string.IsNullOrWhiteSpace(search)) normalized[SearchParameter] = search.Trim();
        if (ParseEnum<UserRole>(filters.GetValueOrDefault(UserRoleParameter)) is { } role) normalized[UserRoleParameter] = role.ToString();
        if (ParseEnum<UserStatus>(filters.GetValueOrDefault(UserStatusParameter)) is { } status) normalized[UserStatusParameter] = status.ToString();
        if (ParseDate(filters.GetValueOrDefault(StartDateParameter)) is { } startDate) normalized[StartDateParameter] = startDate.ToString(DateFormat, CultureInfo.InvariantCulture);
        if (ParseDate(filters.GetValueOrDefault(EndDateParameter)) is { } endDate) normalized[EndDateParameter] = endDate.ToString(DateFormat, CultureInfo.InvariantCulture);
        return normalized;
    }

    // The API pages by PageOffset and rejects an explicit offset on an empty result, so the first page omits it. A calendar
    // date is sent as the date itself, at midnight UTC.
    public static GetUsersQuery ToQuery(DataListRequest request)
    {
        return new GetUsersQuery(
            request.Filters.GetValueOrDefault(SearchParameter),
            ParseEnum<UserRole>(request.Filters.GetValueOrDefault(UserRoleParameter)),
            ParseEnum<UserStatus>(request.Filters.GetValueOrDefault(UserStatusParameter)),
            ToDateTimeOffset(ParseDate(request.Filters.GetValueOrDefault(StartDateParameter))),
            ToDateTimeOffset(ParseDate(request.Filters.GetValueOrDefault(EndDateParameter))),
            ParseEnum<SortableUserProperties>(request.OrderBy) ?? SortableUserProperties.Name,
            request.SortOrder,
            request.PageOffset == 0 ? null : request.PageOffset,
            request.PageSize
        );
    }

    public static async Task<DataListFetchResult<UserDetails>> FetchAsync(UsersClient usersClient, DataListRequest request, CancellationToken cancellationToken)
    {
        var result = await usersClient.GetUsersAsync(ToQuery(request), cancellationToken);
        if (result.IsSuccess) return DataListFetchResult<UserDetails>.Success(result.Value.Users, result.Value.TotalCount);
        var failure = ApiFailureClassifier.Classify(result);
        return DataListFetchResult<UserDetails>.Failure(result.Problem?.StatusCode, failure.Message ?? result.Outcome.ToString());
    }

    // Names only: Enum.TryParse would also accept numbers
    private static TEnum? ParseEnum<TEnum>(string? value) where TEnum : struct, Enum
    {
        return Enum.GetValues<TEnum>().Cast<TEnum?>().FirstOrDefault(candidate => string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase));
    }

    private static DateOnly? ParseDate(string? value)
    {
        return DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    private static DateTimeOffset? ToDateTimeOffset(DateOnly? date)
    {
        return date is { } value ? new DateTimeOffset(value, TimeOnly.MinValue, TimeSpan.Zero) : null;
    }
}
