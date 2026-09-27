// The user detail's Sessions tab's side of DataList: one page of the person's sessions across every account the person is a
// member of, as GET /api/back-office/users/{id}/sessions pages them. The tab's page parameter carries the sessions prefix,
// and the list id carries the user id. The account API orders the sessions itself, so the list has no sort keys.

using Account.Client;
using Account.Features.Users.BackOffice.Queries;
using Account.Features.Users.BackOffice.Requests;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;
using SharedKernel.Domain;

namespace Blazor.Client.BackOffice.Users;

public static class UserSessionsListSource
{
    public const string ParameterPrefix = "sessions";

    // No sort keys: the default is never written and an orderBy in the URL is ignored
    public const string DefaultOrderBy = "default";

    public static string ListId(UserId userId)
    {
        return $"back-office-user-sessions-{userId.Value}";
    }

    public static GetBackOfficeUserSessionsQuery ToQuery(DataListRequest request)
    {
        return new GetBackOfficeUserSessionsQuery(request.PageOffset, request.PageSize);
    }

    public static async Task<DataListFetchResult<BackOfficeUserSession>> FetchAsync(BackOfficeClient backOfficeClient, UserId userId, DataListRequest request, CancellationToken cancellationToken)
    {
        var result = await backOfficeClient.GetUserSessionsAsync(userId, ToQuery(request), cancellationToken);
        if (result.IsSuccess) return DataListFetchResult<BackOfficeUserSession>.Success(result.Value.Sessions, result.Value.TotalCount);
        var failure = ApiFailureClassifier.Classify(result);
        return DataListFetchResult<BackOfficeUserSession>.Failure(result.Problem?.StatusCode, failure.Message ?? result.Outcome.ToString());
    }
}
