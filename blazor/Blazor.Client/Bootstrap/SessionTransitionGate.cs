// The transport half of a session transition, a logout or a tenant switch: which request is the transition's own, and
// whether its successful response has replaced the identity this runtime was started with. SessionTransitionHandler
// enforces it on every account API call; SessionTransition in Blazor.Client.Session starts and ends it. It holds no typed
// client, so the HttpClient pipeline can depend on it.

namespace Blazor.Client.Bootstrap;

public sealed class SessionTransitionGate
{
    private string? _transitionPath;

    // A transition is in flight: only its own request and safe reads leave the browser
    public bool IsActive => _transitionPath is not null;

    // The transition's own request succeeded, so the session cookies now belong to another identity or to none
    public bool IsIdentityReplaced { get; private set; }

    public bool TryBegin(string transitionPath)
    {
        if (_transitionPath is not null || IsIdentityReplaced) return false;

        _transitionPath = transitionPath;
        return true;
    }

    public void End()
    {
        _transitionPath = null;
    }

    public bool IsTransitionRequest(HttpRequestMessage request)
    {
        return _transitionPath is not null && request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == _transitionPath;
    }

    public void MarkIdentityReplaced()
    {
        IsIdentityReplaced = true;
    }
}
