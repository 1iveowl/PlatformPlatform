using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Diagnostics;

namespace Blazor.Host.Shell;

// Marks a back-office page: a page that hosts a WebAssembly component, like [InteractiveSurface], but is served only on the
// back-office host, under that origin's own policy, and without the app's offline shell, manifest or service worker.
[AttributeUsage(AttributeTargets.Class)]
public sealed class BackOfficeSurfaceAttribute : Attribute;

// Marks the back office's not-found page, the answer to every unknown path below the back office's home. Its status becomes
// 404 only as the response starts: a page that answers 404 while it renders is replaced by the framework's not-found
// handling, which re-executes the app's not-found page, and that page answers an empty 404 on the back-office host.
[AttributeUsage(AttributeTargets.Class)]
public sealed class BackOfficeNotFoundAttribute : Attribute;

// Marks a back-office page that exists only with the subscription setting on (the invoices and billing events lists), as the
// React back office's requireSubscriptionEnabled guard has it. With the setting off the page renders the back office's
// not-found content, and its status becomes 404 as the response starts, as for BackOfficeNotFoundAttribute.
[AttributeUsage(AttributeTargets.Class)]
public sealed class BackOfficeSubscriptionPageAttribute : Attribute;

// The back-office origin from BACK_OFFICE_PUBLIC_URL, which the account API's back-office listener names in X-Forwarded-Host
// when it forwards a back-office page here. Unset, the host has no back-office host and serves no back-office page.
public sealed class BackOfficeOrigin
{
    public const string PublicUrlKey = "BACK_OFFICE_PUBLIC_URL";

    public BackOfficeOrigin()
    {
        PublicUrl = Uri.TryCreate(Environment.GetEnvironmentVariable(PublicUrlKey), UriKind.Absolute, out var publicUrl) ? publicUrl : null;
    }

    public Uri? PublicUrl { get; }

    public bool IsBackOfficeHost(HttpRequest request)
    {
        return PublicUrl is not null && request.Host.Host.Equals(PublicUrl.IdnHost, StringComparison.OrdinalIgnoreCase);
    }
}

public static class BackOfficeSurface
{
    // After routing. On the back-office host only back-office pages and the files they load are served: every other page,
    // public or authenticated, and the app's manifest and service worker answer a plain 404, so the back-office origin can
    // neither show an app page nor register the app's worker. A back-office page answers 404 on any other host.
    public static Task RestrictToSurfaceHostAsync(HttpContext context, RequestDelegate next, BackOfficeOrigin backOfficeOrigin, BackOfficeSettings backOfficeSettings)
    {
        var endpoint = context.GetEndpoint();
        var isBackOfficePage = endpoint?.Metadata.GetMetadata<BackOfficeSurfaceAttribute>() is not null;

        if (!backOfficeOrigin.IsBackOfficeHost(context.Request))
        {
            if (!isBackOfficePage) return next(context);

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        var isComponentPage = endpoint?.Metadata.GetMetadata<ComponentTypeMetadata>() is not null;
        var isAppOnlyFile = context.Request.Path.Equals(new PathString(OfflineShell.WorkerPath)) || context.Request.Path.Equals(new PathString(HostShell.ManifestPath));
        var isHiddenSubscriptionPage = !backOfficeSettings.IsSubscriptionEnabled && endpoint?.Metadata.GetMetadata<BackOfficeSubscriptionPageAttribute>() is not null;
        if (endpoint?.Metadata.GetMetadata<BackOfficeNotFoundAttribute>() is not null || isHiddenSubscriptionPage)
        {
            // The rendered page is the answer, sent with 404 when its headers go out; a challenge's redirect stays as it is
            context.Response.OnStarting(() =>
                {
                    if (context.Response.StatusCode == StatusCodes.Status200OK) context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
                }
            );
        }

        if (isBackOfficePage || (!isComponentPage && !isAppOnlyFile)) return next(context);

        // Plain, without the not-found page, which renders the app's navigation
        context.Features.Get<IStatusCodePagesFeature>()?.Enabled = false;
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }
}
