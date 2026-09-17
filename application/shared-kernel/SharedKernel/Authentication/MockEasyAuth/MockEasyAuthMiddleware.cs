using System.Net;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Authentication.BackOfficeIdentity;
using SharedKernel.Navigation;

namespace SharedKernel.Authentication.MockEasyAuth;

public sealed class MockEasyAuthMiddleware(RequestDelegate next)
{
    private const string MockLoginPath = "/login";

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var method = context.Request.Method;

        if (HttpMethods.IsGet(method) && path.Equals(BackOfficeIdentityDefaults.LoginPath, StringComparison.OrdinalIgnoreCase))
        {
            RedirectToMockLoginPage(context);
            return;
        }

        if (HttpMethods.IsGet(method) && path.Equals(BackOfficeIdentityDefaults.CallbackPath, StringComparison.OrdinalIgnoreCase))
        {
            HandleCallback(context);
            return;
        }

        if (HttpMethods.IsGet(method) && path.Equals(BackOfficeIdentityDefaults.LogoutPath, StringComparison.OrdinalIgnoreCase))
        {
            HandleLogout(context);
            return;
        }

        InjectClientPrincipalHeaders(context);
        await next(context);
    }

    private static void InjectClientPrincipalHeaders(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(MockEasyAuthCookie.CookieName, out var identityId)) return;
        var identity = MockEasyAuthIdentities.Find(identityId);
        if (identity is null) return;

        context.Request.Headers[BackOfficeIdentityDefaults.PrincipalNameHeader] = identity.Name;
        context.Request.Headers[BackOfficeIdentityDefaults.PrincipalIdHeader] = identity.ObjectId;
        context.Request.Headers[BackOfficeIdentityDefaults.PrincipalPayloadHeader] = MockEasyAuthCookie.EncodePayload(identity);
    }

    private static void RedirectToMockLoginPage(HttpContext context)
    {
        var redirect = context.Request.Query["post_login_redirect_uri"].ToString();
        if (string.IsNullOrEmpty(redirect) || !LocalReturnPath.IsValid(redirect)) redirect = "/";

        // Belt-and-suspenders: if the post-login target is itself the picker, send the user home after
        // sign-in to prevent a feedback loop if some upstream component starts gating the picker again.
        if (redirect.Equals(MockLoginPath, StringComparison.OrdinalIgnoreCase) ||
            redirect.StartsWith($"{MockLoginPath}?", StringComparison.OrdinalIgnoreCase) ||
            redirect.StartsWith($"{MockLoginPath}/", StringComparison.OrdinalIgnoreCase))
        {
            redirect = "/";
        }

        context.Response.Redirect($"{MockLoginPath}?returnPath={UrlEncoder.Default.Encode(redirect)}");
    }

    private static void HandleCallback(HttpContext context)
    {
        var identityId = context.Request.Query["identity"].ToString();
        var redirect = SafeRelativeRedirect(context.Request.Query["post_login_redirect_uri"].ToString());

        var identity = MockEasyAuthIdentities.Find(identityId);
        if (identity is null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return;
        }

        context.Response.Cookies.Append(MockEasyAuthCookie.CookieName, identity.Id, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            }
        );
        context.Response.Redirect(redirect);
    }

    private static void HandleLogout(HttpContext context)
    {
        context.Response.Cookies.Delete(MockEasyAuthCookie.CookieName, new CookieOptions { Secure = true, Path = "/" });
        var redirect = SafeRelativeRedirect(context.Request.Query["post_logout_redirect_uri"].ToString());
        context.Response.Redirect(redirect);
    }

    // Returns a guaranteed safe in-app redirect target. Anything that is not a single-leading-slash
    // relative path (rejecting protocol-relative "//", schemes, traversal, backslashes, etc.) falls back
    // to "/", so the redirect callers below cannot send the browser to an attacker-controlled URL.
    private static string SafeRelativeRedirect(string candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return "/";
        if (!candidate.StartsWith('/')) return "/";
        if (candidate.StartsWith("//", StringComparison.Ordinal)) return "/";
        if (!LocalReturnPath.IsValid(candidate)) return "/";
        return candidate;
    }
}

public static class MockEasyAuthMiddlewareExtensions
{
    extension(IApplicationBuilder app)
    {
        public IApplicationBuilder UseMockEasyAuthInDevelopment()
        {
            var environment = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();
            return environment.IsDevelopment() ? app.UseMiddleware<MockEasyAuthMiddleware>() : app;
        }
    }
}
