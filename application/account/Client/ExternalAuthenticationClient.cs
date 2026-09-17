using Account.Features.ExternalAuthentication.Commands;
using Account.Features.ExternalAuthentication.Domain;
using Account.Features.ExternalAuthentication.Queries;
using Account.Features.ExternalAuthentication.Requests;

namespace Account.Client;

// The identity verification calls of a signed-in user. The external login and signup starts are document navigations and
// have no method here (AccountApiRoutes.StartExternalLogin and StartExternalSignup).
public sealed class ExternalAuthenticationClient(HttpClient httpClient)
{
    private readonly AccountApiTransport _transport = new(httpClient);

    public Task<ApiCallResult<VerificationStatusResponse>> GetVerificationStatusAsync(CancellationToken cancellationToken)
    {
        return _transport.GetAsync<VerificationStatusResponse>(AccountApiRoutes.VerificationStatus, cancellationToken);
    }

    // A state-changing POST, so the registered chain adds the antiforgery token; the response's authorization URL is where
    // the caller navigates the whole document
    public Task<ApiCallResult<StartExternalVerificationResponse>> StartVerificationAsync(ExternalProviderType provider, string edition, string returnPath, CancellationToken cancellationToken)
    {
        return _transport.SendAsync<StartExternalVerificationCommand, StartExternalVerificationResponse>(
            HttpMethod.Post, AccountApiRoutes.StartExternalVerification(provider, edition), new StartExternalVerificationCommand(returnPath), cancellationToken
        );
    }
}
