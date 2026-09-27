// Whether the account detail has its account: the account API answers 404 for a tenant id it does not know, which the page
// shows as the back office's not-found state inside the shell; any other failure is presented as a failure, and the page
// shows neither the account nor the not-found state for it.

using Account.Client;

namespace Blazor.Client.BackOffice.Accounts;

public enum AccountDetailStatus
{
    Loading,
    Loaded,
    NotFound,
    Failed
}

public static class AccountDetailState
{
    private const int NotFoundStatusCode = 404;

    public static AccountDetailStatus FromResult<TValue>(ApiCallResult<TValue> result)
    {
        if (result.IsSuccess) return AccountDetailStatus.Loaded;
        return result.Problem?.StatusCode == NotFoundStatusCode ? AccountDetailStatus.NotFound : AccountDetailStatus.Failed;
    }
}
