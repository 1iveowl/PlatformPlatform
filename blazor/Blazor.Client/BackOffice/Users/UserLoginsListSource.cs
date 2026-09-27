// The user detail's Logins tab's side of DataList: every sign-in attempt of the last 30 days as
// GET /api/back-office/users/{id}/login-history returns them, newest first, read once and paged in memory. An attempt has no
// id of its own, so each row carries its position in the history as its key. The tab's page parameter carries the logins
// prefix, and the list id carries the user id.

using System.Globalization;
using Account.Features.Users.BackOffice.Queries;
using Blazor.Client.Components.Lists;
using SharedKernel.Domain;

namespace Blazor.Client.BackOffice.Users;

public sealed record UserLoginRow(int Position, BackOfficeUserLoginEntry Entry);

public static class UserLoginsListSource
{
    public const string ParameterPrefix = "logins";

    // No sort keys: the attempts keep the account API's order, newest first
    public const string DefaultOrderBy = "default";

    public static string ListId(UserId userId)
    {
        return $"back-office-user-logins-{userId.Value}";
    }

    public static IReadOnlyList<UserLoginRow> ToRows(IReadOnlyList<BackOfficeUserLoginEntry> entries)
    {
        return entries.Select((entry, position) => new UserLoginRow(position, entry)).ToArray();
    }

    public static DataListFetchResult<UserLoginRow> Page(IReadOnlyList<UserLoginRow> rows, DataListRequest request)
    {
        return DataListFetchResult<UserLoginRow>.Success(rows.Skip(request.PageOffset * request.PageSize).Take(request.PageSize).ToArray(), rows.Count);
    }

    public static string KeyOf(UserLoginRow row)
    {
        return row.Position.ToString(CultureInfo.InvariantCulture);
    }
}
