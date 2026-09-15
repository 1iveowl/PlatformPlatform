// Spike code (Blazor edition, stage B3): the users mutations and the single-user lookup of the users surface, as a
// transitional facade over the typed UsersClient. The list pages and their cache are DataList's (see UsersListSource).

using Account.Client;
using Account.Features.Users.Requests;
using SharedKernel.Domain;

namespace Blazor.Client.Users;

public sealed class UsersApiException(int? statusCode, string message)
    : Exception($"The account API returned {statusCode?.ToString() ?? "no response"}: {message}")
{
    public int? StatusCode { get; } = statusCode;

    public static UsersApiException FromProblem(ApiCallOutcome outcome, ApiCallProblem problem)
    {
        var fieldErrors = problem.Errors.Values.SelectMany(messages => messages).ToArray();
        var message = fieldErrors.Length > 0 ? string.Join(" ", fieldErrors) : problem.Detail ?? problem.Title ?? outcome.ToString();
        return new UsersApiException(problem.StatusCode, message);
    }
}

public sealed class UsersApiClient(UsersClient usersClient)
{
    public async Task<UserDetails> GetUserAsync(string id)
    {
        return EnsureSuccess(await usersClient.GetUserAsync(new UserId(id), CancellationToken.None));
    }

    public async Task ChangeRoleAsync(string id, UserRole role)
    {
        EnsureSuccess(await usersClient.ChangeUserRoleAsync(new UserId(id), new ChangeUserRoleCommand { UserRole = role }, CancellationToken.None));
    }

    public async Task DeleteAsync(string id)
    {
        EnsureSuccess(await usersClient.DeleteUserAsync(new UserId(id), CancellationToken.None));
    }

    public async Task BulkDeleteAsync(IEnumerable<string> ids)
    {
        EnsureSuccess(await usersClient.BulkDeleteUsersAsync(new BulkDeleteUsersCommand(ids.Select(id => new UserId(id)).ToArray()), CancellationToken.None));
    }

    private static TValue EnsureSuccess<TValue>(ApiCallResult<TValue> result)
    {
        return result.IsSuccess ? result.Value : throw UsersApiException.FromProblem(result.Outcome, result.Problem);
    }

    private static void EnsureSuccess(ApiCallResult result)
    {
        if (!result.IsSuccess) throw UsersApiException.FromProblem(result.Outcome, result.Problem);
    }
}
