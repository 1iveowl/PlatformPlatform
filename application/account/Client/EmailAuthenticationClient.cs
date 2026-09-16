using Account.Features.EmailAuthentication.Commands;
using Account.Features.EmailAuthentication.Domain;
using Account.Features.EmailAuthentication.Requests;

namespace Account.Client;

public sealed class EmailAuthenticationClient(HttpClient httpClient)
{
    private readonly AccountApiTransport _transport = new(httpClient);

    public Task<ApiCallResult<StartEmailLoginResponse>> StartLoginAsync(StartEmailLoginCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync<StartEmailLoginCommand, StartEmailLoginResponse>(HttpMethod.Post, AccountApiRoutes.StartEmailLogin, command, cancellationToken);
    }

    public Task<ApiCallResult> CompleteLoginAsync(EmailLoginId emailLoginId, CompleteEmailLoginCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Post, AccountApiRoutes.CompleteEmailLogin(emailLoginId), command, cancellationToken);
    }

    public Task<ApiCallResult<StartEmailSignupResponse>> StartSignupAsync(StartEmailSignupCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync<StartEmailSignupCommand, StartEmailSignupResponse>(HttpMethod.Post, AccountApiRoutes.StartEmailSignup, command, cancellationToken);
    }

    public Task<ApiCallResult> CompleteSignupAsync(EmailLoginId emailLoginId, CompleteEmailSignupCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Post, AccountApiRoutes.CompleteEmailSignup(emailLoginId), command, cancellationToken);
    }

    public Task<ApiCallResult<ResendEmailLoginCodeResponse>> ResendLoginCodeAsync(EmailLoginId emailLoginId, CancellationToken cancellationToken)
    {
        return _transport.SendAsync<ResendEmailLoginCodeResponse>(HttpMethod.Post, AccountApiRoutes.ResendEmailLoginCode(emailLoginId), cancellationToken);
    }

    public Task<ApiCallResult<ResendEmailLoginCodeResponse>> ResendSignupCodeAsync(EmailLoginId emailLoginId, CancellationToken cancellationToken)
    {
        return _transport.SendAsync<ResendEmailLoginCodeResponse>(HttpMethod.Post, AccountApiRoutes.ResendEmailSignupCode(emailLoginId), cancellationToken);
    }
}
