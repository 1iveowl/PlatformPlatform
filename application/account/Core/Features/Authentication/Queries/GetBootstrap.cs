using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using SharedKernel.Authentication;
using SharedKernel.Cqrs;
using SharedKernel.ExecutionContext;

namespace Account.Features.Authentication.Queries;

[PublicAPI]
public sealed record GetBootstrapQuery : IRequest<Result<BootstrapResponse>>;

// Reaches into HttpContext for the three things the bootstrap contract carries that are not user data: the response
// must never be stored, the antiforgery pair is issued per browser, and the request's Authorization header tells a
// rejected credential apart from a caller that sent none. Tokens are issued through IAntiforgeryTokenIssuer rather
// than IAntiforgery, because this handler is registered in the worker host too, which has no antiforgery services.
public sealed class GetBootstrapHandler(IExecutionContext executionContext, IHttpContextAccessor httpContextAccessor, IAntiforgeryTokenIssuer antiforgeryTokenIssuer)
    : IRequestHandler<GetBootstrapQuery, Result<BootstrapResponse>>
{
    private static readonly string ApplicationVersion =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
        ?? string.Empty;

    public Task<Result<BootstrapResponse>> Handle(GetBootstrapQuery query, CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext!;
        httpContext.Response.Headers.CacheControl = "no-store";

        var userInfo = executionContext.UserInfo;
        if (!userInfo.IsAuthenticated && httpContext.Request.Headers.Authorization.Count > 0)
        {
            // A missing, malformed or expired bearer token is not an anonymous visit; the client must renew or sign in again
            return Task.FromResult(Result<BootstrapResponse>.Unauthorized("The access token is not valid."));
        }

        var user = userInfo.IsAuthenticated
            ? new BootstrapUser(
                userInfo.Id!,
                userInfo.TenantId!,
                userInfo.Role,
                userInfo.Email,
                NullIfEmpty(userInfo.FirstName),
                NullIfEmpty(userInfo.LastName),
                NullIfEmpty(userInfo.Title),
                NullIfEmpty(userInfo.AvatarUrl),
                NullIfEmpty(userInfo.TenantName),
                NullIfEmpty(userInfo.TenantLogoUrl),
                NullIfEmpty(userInfo.SubscriptionPlan),
                userInfo.IsInternalUser,
                userInfo.FeatureFlags.Order(StringComparer.Ordinal).ToArray()
            )
            : null;

        var response = new BootstrapResponse(
            userInfo.IsAuthenticated,
            user,
            userInfo.Locale!,
            BootstrapConfiguration.CreateRuntimeConfiguration(Environment.GetEnvironmentVariable, ApplicationVersion),
            BootstrapConfiguration.CreateSystemFeatureFlags(Environment.GetEnvironmentVariable),
            antiforgeryTokenIssuer.IssueRequestToken(httpContext)
        );

        return Task.FromResult<Result<BootstrapResponse>>(response);
    }

    // The access token carries an empty string for an unset optional claim
    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
