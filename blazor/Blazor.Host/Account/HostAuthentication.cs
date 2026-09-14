// Bearer authentication for the host. The gateway turns the session cookies into a bearer token before a request
// reaches this host, and the token is validated with the platform's token signing service, so the host trusts exactly
// the tokens the account API trusts: the development signing key locally and the Key Vault key in Azure.

using Blazor.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using SharedKernel.Authentication.TokenSigning;

namespace Blazor.Host.Account;

public static class HostAuthentication
{
    // The same values the APIs use in ApiDependencyConfiguration
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(5);

    extension(IServiceCollection services)
    {
        public IServiceCollection AddHostAuthentication(ITokenSigningClient tokenSigningClient)
        {
            services.AddSingleton(tokenSigningClient);
            services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                    {
                        options.TokenValidationParameters = tokenSigningClient.GetTokenValidationParameters(ClockSkew, true);
                        options.Events = new JwtBearerEvents { OnChallenge = ChallengeAsync };
                    }
                );

            return services.AddAuthorization();
        }
    }

    public static bool IsApiRequest(HttpRequest request)
    {
        if (request.Path.StartsWithSegments("/api")) return true;

        return request.GetTypedHeaders().Accept.Any(accept => accept.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase));
    }

    // A page request goes to the login page, which returns to the sanitized path after sign-in; an API request gets 401
    private static Task ChallengeAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        var request = context.Request;

        if (IsApiRequest(request))
        {
            // Without this, the status code page re-execution would answer the API client with the not-found document
            context.HttpContext.Features.Get<IStatusCodePagesFeature>()?.Enabled = false;
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        var returnPath = AppUrls.SanitizeReturnPath($"{request.PathBase}{request.Path}{request.QueryString}");
        context.Response.Redirect($"{AppUrls.ToAbsolute("login")}?returnPath={Uri.EscapeDataString(returnPath)}");
        return Task.CompletedTask;
    }
}
