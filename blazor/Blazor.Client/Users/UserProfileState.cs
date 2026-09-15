// The side pane's state: which user it shows, the authoritative copy of that user, and the notices that compare it with
// the list's current page. Every load is numbered and only the latest one is applied, so a response that started before a
// role change, a close or another row cannot put older data back on screen.

using Account.Client;
using Blazor.Client.Forms;

namespace Blazor.Client.Users;

public enum UserProfileStatus
{
    Closed,
    Loading,
    Ready,

    // The user does not exist, was deleted or is not visible to the caller; nothing about the user is shown
    NotFound,

    Failed
}

public sealed class UserProfileState
{
    private long _version;

    public string? UserId { get; private set; }

    public UserDetails? User { get; private set; }

    public UserProfileStatus Status { get; private set; } = UserProfileStatus.Closed;

    public string? ErrorMessage { get; private set; }

    public bool IsOpen => UserId is not null;

    // Starts a load for the user. A row the list already shows is displayed at once while the authoritative copy loads;
    // the returned version identifies this load for Complete.
    public long Open(string userId, UserDetails? rowUser = null)
    {
        _version++;
        if (UserId != userId)
        {
            User = null;
            ErrorMessage = null;
        }

        UserId = userId;
        if (rowUser is not null && rowUser.Id.Value == userId && User is null) User = rowUser;
        Status = User is null ? UserProfileStatus.Loading : UserProfileStatus.Ready;
        return _version;
    }

    // Applies a load's result when it is still the latest load; returns whether anything changed
    public bool Complete(long version, ApiCallResult<UserDetails> result)
    {
        if (version != _version || UserId is null) return false;

        if (result.IsSuccess)
        {
            if (result.Value.Id.Value != UserId) return false;
            User = result.Value;
            Status = UserProfileStatus.Ready;
            ErrorMessage = null;
            return true;
        }

        var failure = ApiFailureClassifier.Classify(result);
        if (failure.Kind == ApiFailureKind.Suppressed) return false;

        User = null;
        if (result.Problem.StatusCode is 400 or 403 or 404)
        {
            Status = UserProfileStatus.NotFound;
            ErrorMessage = null;
        }
        else
        {
            Status = UserProfileStatus.Failed;
            ErrorMessage = failure.Message ?? string.Join(" ", result.Problem.Errors.Values.SelectMany(messages => messages));
        }

        return true;
    }

    public void Close()
    {
        _version++;
        UserId = null;
        User = null;
        ErrorMessage = null;
        Status = UserProfileStatus.Closed;
    }

    // The shown user is not a row of the list's loaded page, for example after a deep link or a search
    public bool IsNotInCurrentView(IReadOnlyCollection<UserDetails> pageItems)
    {
        return User is not null && pageItems.All(item => item.Id != User.Id);
    }

    // The pane's copy was modified at a different time than the row the list shows for the same user
    public bool IsDataUpdated(IReadOnlyCollection<UserDetails> pageItems)
    {
        if (User?.ModifiedAt is not { } modifiedAt) return false;
        return pageItems.FirstOrDefault(item => item.Id == User.Id)?.ModifiedAt is { } rowModifiedAt && rowModifiedAt != modifiedAt;
    }
}
