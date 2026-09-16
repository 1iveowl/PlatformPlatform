namespace Account.Client;

// Supplies the antiforgery token the account API bootstrap issued; the registrant decides where it comes from
public interface IAntiforgeryTokenSource
{
    ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken);
}

// The gateway validates x-xsrf-token on every state-changing cookie request. Safe methods never carry it, so a read does
// not even ask the source for a token.
public sealed class AntiforgeryHeaderHandler(IAntiforgeryTokenSource antiforgeryTokenSource) : DelegatingHandler
{
    private static readonly HttpMethod[] SafeMethods = [HttpMethod.Get, HttpMethod.Head, HttpMethod.Options, HttpMethod.Trace];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (SafeMethods.Contains(request.Method) || request.Headers.Contains(AccountApiHeaders.AntiforgeryToken))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var antiforgeryToken = await antiforgeryTokenSource.GetTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(antiforgeryToken))
        {
            request.Headers.Add(AccountApiHeaders.AntiforgeryToken, antiforgeryToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
