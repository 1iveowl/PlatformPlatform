// Back-office authentication for the host. A back-office page is served only on the back-office host, which reaches this host
// only through the account API's back-office listener: the platform authentication in front of that listener (Easy Auth in
// Azure, the mock locally) signs the person in, the listener authenticates the principal headers with the account API's own
// handler and forwards the resulting identity as ForwardedBackOfficeIdentity, protected by the shared data protection key
// ring. This scheme reads that header and nothing else. The X-MS-CLIENT-PRINCIPAL headers are never read here, so a request
// that reaches this host any other way (the app gateway, a forged X-Forwarded-Host, a direct call to this port) carries no
// back-office identity. The app user's bearer token is a different scheme that back-office pages never accept, and this
// scheme answers only on the back-office host, so a back-office identity never authenticates an app page.

using System.Net;
using System.Text.Encodings.Web;
using Blazor.Host.Shell;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using SharedKernel.Authentication.BackOfficeIdentity;

namespace Blazor.Host.Account;

public static class BackOfficeAuthentication
{
    public const string SchemeName = "BackOfficeIdentity";

    public const string PolicyName = "BackOffice";

    // The platform authentication's login route on the back-office host; the local mock serves the same path
    public const string LoginPath = "/.auth/login/aad";

    extension(IServiceCollection services)
    {
        public IServiceCollection AddBackOfficeAuthentication()
        {
            services
                .AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, BackOfficeAuthenticationHandler>(SchemeName, _ => { });

            return services.AddAuthorization(options => options.AddPolicy(PolicyName, policy =>
                    {
                        policy.AuthenticationSchemes = [SchemeName];
                        policy.RequireAuthenticatedUser();
                    }
                )
            );
        }
    }
}

public sealed class BackOfficeAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IDataProtectionProvider dataProtectionProvider,
    BackOfficeOrigin backOfficeOrigin
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!backOfficeOrigin.IsBackOfficeHost(Request)) return Task.FromResult(AuthenticateResult.NoResult());

        var protectedIdentity = Request.Headers[ForwardedBackOfficeIdentity.HeaderName].ToString();
        if (protectedIdentity.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());

        var principal = ForwardedBackOfficeIdentity.Unprotect(dataProtectionProvider, protectedIdentity, BackOfficeAuthentication.SchemeName);
        return Task.FromResult(principal is null
            ? AuthenticateResult.Fail("The forwarded back-office identity is not valid.")
            : AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name))
        );
    }

    // A page request goes to the platform's login on the back-office host, which returns to the page after sign-in; an API
    // request gets 401 without the not-found document
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (HostAuthentication.IsApiRequest(Request))
        {
            Context.Features.Get<IStatusCodePagesFeature>()?.Enabled = false;
            Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return Task.CompletedTask;
        }

        var returnPath = $"{Request.PathBase}{Request.Path}{Request.QueryString}";
        Response.Redirect($"{BackOfficeAuthentication.LoginPath}?post_login_redirect_uri={Uri.EscapeDataString(returnPath)}");
        return Task.CompletedTask;
    }
}
