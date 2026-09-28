using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace SharedKernel.Authentication.BackOfficeIdentity;

public sealed class BackOfficeIdentityHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    BackOfficeListener backOfficeListener
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (PrefersHtml(Request))
        {
            var redirectTarget = $"{Request.Path}{Request.QueryString}";
            var loginUrl = $"{BackOfficeIdentityDefaults.LoginPath}?post_login_redirect_uri={Uri.EscapeDataString(redirectTarget)}";
            Response.Redirect(loginUrl);
            return Task.CompletedTask;
        }

        Response.StatusCode = (int)HttpStatusCode.Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        if (PrefersHtml(Request))
        {
            Response.Redirect(BackOfficeIdentityDefaults.AccessDeniedPath);
            return Task.CompletedTask;
        }

        Response.StatusCode = (int)HttpStatusCode.Forbidden;
        return Task.CompletedTask;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // NoResult rather than Fail: a request elsewhere that carries the headers is simply not a back-office identity.
        if (!backOfficeListener.Received(Context))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var headerName = Context.Request.Headers[BackOfficeIdentityDefaults.PrincipalNameHeader].ToString();
        if (string.IsNullOrWhiteSpace(headerName))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var oid = Context.Request.Headers[BackOfficeIdentityDefaults.PrincipalIdHeader].ToString();
        var payload = Context.Request.Headers[BackOfficeIdentityDefaults.PrincipalPayloadHeader].ToString();
        var principal = string.IsNullOrWhiteSpace(payload) ? null : TryDecodePayload(payload);

        var claims = BuildClaims(headerName, oid, principal);
        var identity = new ClaimsIdentity(claims, BackOfficeIdentityDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    // The PrincipalNameHeader carries the UPN/email in real Easy Auth. The friendly display name
    // lives in the payload's OIDC `name` claim, which we prefer when present.
    private static List<Claim> BuildClaims(string headerName, string oid, BackOfficeClientPrincipal? principal)
    {
        var displayName = principal?.Claims?
            .FirstOrDefault(claim => claim.Type == BackOfficeIdentityDefaults.NameClaimType)?
            .Value;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = headerName;
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, displayName) };

        if (!string.IsNullOrWhiteSpace(oid))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, oid));
        }

        if (principal?.Claims is null) return claims;

        foreach (var claim in principal.Claims)
        {
            if (IsReservedClaim(claim.Type)) continue;
            claims.Add(new Claim(claim.Type, claim.Value));
        }

        return claims;
    }

    private static bool IsReservedClaim(string? claimType)
    {
        if (string.IsNullOrEmpty(claimType)) return true;
        if (claimType is ClaimTypes.Name or ClaimTypes.NameIdentifier) return true;
        return claimType == BackOfficeIdentityDefaults.NameClaimType;
    }

    private static bool PrefersHtml(HttpRequest request)
    {
        var accept = request.Headers.Accept.ToString();
        if (string.IsNullOrEmpty(accept)) return false;
        return accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    private static BackOfficeClientPrincipal? TryDecodePayload(string base64Payload)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64Payload);
            var json = Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<BackOfficeClientPrincipal>(json, JsonOptions);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
