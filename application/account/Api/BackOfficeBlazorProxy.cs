using System.Net;
using System.Net.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Extensions;
using SharedKernel.Authentication.BackOfficeIdentity;
using SharedKernel.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace Account.Api;

// Serves the Blazor edition's back-office pages on the back-office host. The platform authentication stays in front of this
// listener (Easy Auth in Azure, MockEasyAuthMiddleware locally), and this route is authorized with the same back-office
// policy as the React back office's API, so only a signed-in back-office identity is forwarded. The request goes to the
// internal Blazor host at BACK_OFFICE_BLAZOR_HOST_URL with the identity in the protected ForwardedBackOfficeIdentity header;
// the principal headers and any inbound copy of that header are removed first, so the Blazor host never sees a value a
// browser wrote. The Blazor host validates its own forms, so this route does not read or validate the request body.
// Unset BACK_OFFICE_BLAZOR_HOST_URL maps nothing, which is how the account-api container app (not the back-office one) runs.
public static class BackOfficeBlazorProxy
{
    public const string BlazorHostUrlKey = "BACK_OFFICE_BLAZOR_HOST_URL";

    private const string PathBase = "/blazor";

    private static readonly string[] RemovedRequestHeaders =
    [
        BackOfficeIdentityDefaults.PrincipalNameHeader,
        BackOfficeIdentityDefaults.PrincipalIdHeader,
        BackOfficeIdentityDefaults.PrincipalPayloadHeader,
        ForwardedBackOfficeIdentity.HeaderName,
        "X-Forwarded-For",
        "X-Forwarded-Host",
        "X-Forwarded-Proto",
        "X-Forwarded-Prefix"
    ];

    public static WebApplication MapBackOfficeBlazorProxy(this WebApplication app, string backOfficeHostname)
    {
        var blazorHostUrl = Environment.GetEnvironmentVariable(BlazorHostUrlKey);
        if (string.IsNullOrWhiteSpace(blazorHostUrl)) return app;

        var forwarder = app.Services.GetRequiredService<IHttpForwarder>();
        var invoker = new HttpMessageInvoker(CreateHandler());
        var transformer = new BackOfficeIdentityTransformer();

        app.Map($"{PathBase}/{{**catch-all}}", async (HttpContext context, IAuthorizationService authorizationService, IDataProtectionProvider dataProtectionProvider) =>
                {
                    var adminResult = await authorizationService.AuthorizeAsync(context.User, BackOfficeIdentityDefaults.AdminPolicyName);
                    context.Items[ForwardedBackOfficeIdentity.HeaderName] = ForwardedBackOfficeIdentity.Protect(dataProtectionProvider, context.User, adminResult.Succeeded);

                    var error = await forwarder.SendAsync(context, blazorHostUrl, invoker, ForwarderRequestConfig.Empty, transformer);
                    if (error == ForwarderError.None || context.Response.HasStarted) return;

                    app.Logger.LogWarning(
                        context.Features.Get<IForwarderErrorFeature>()?.Exception,
                        "Back-office Blazor proxy failed for {Url}: {Error}",
                        context.Request.GetDisplayUrl(),
                        error
                    );
                    context.Response.StatusCode = StatusCodes.Status502BadGateway;
                }
            )
            .RequireHost(backOfficeHostname)
            .RequireAuthorization(BackOfficeIdentityDefaults.PolicyName)
            .DisableAntiforgery()
            .ExcludeFromDescription();

        return app;
    }

    private static SocketsHttpHandler CreateHandler()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false
        };

        if (!SharedInfrastructureConfiguration.IsRunningInAzure)
        {
            // The Blazor host listens with ASP.NET Core's localhost development certificate (CN=localhost), as the rsbuild
            // dev server does for BackOfficeDevStaticProxy; accept that certificate even when only its chain is untrusted
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, errors)
                    => errors == SslPolicyErrors.None || (errors == SslPolicyErrors.RemoteCertificateChainErrors && certificate?.Subject == "CN=localhost")
            };
        }

        return handler;
    }

    // Copies the request as the default transformer does, then replaces every identity and forwarding header with the values
    // this listener established: the protected identity, the back-office host the route matched, the scheme and the client
    // address the forwarded headers middleware accepted
    private sealed class BackOfficeIdentityTransformer : HttpTransformer
    {
        public override async ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken cancellationToken)
        {
            await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);

            foreach (var header in RemovedRequestHeaders)
            {
                proxyRequest.Headers.Remove(header);
            }

            proxyRequest.Headers.TryAddWithoutValidation(ForwardedBackOfficeIdentity.HeaderName, (string)httpContext.Items[ForwardedBackOfficeIdentity.HeaderName]!);
            proxyRequest.Headers.TryAddWithoutValidation("X-Forwarded-Host", httpContext.Request.Host.Value);
            proxyRequest.Headers.TryAddWithoutValidation("X-Forwarded-Proto", httpContext.Request.Scheme);
            if (httpContext.Connection.RemoteIpAddress is { } clientAddress)
            {
                proxyRequest.Headers.TryAddWithoutValidation("X-Forwarded-For", clientAddress.ToString());
            }
        }
    }
}
