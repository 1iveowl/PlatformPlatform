// The Blazor host behind AppGateway under the path base in AppUrls. It authenticates the gateway's bearer token, serves
// the static server-rendered public surface and the prerendered WebAssembly pages with the React shell's headers, and
// serves the brand stylesheet, the web app manifest and the temporary bootstrap endpoint.

using System.Net;
using System.Runtime.InteropServices;
using Blazor.Client;
using Blazor.Client.Bootstrap;
using Blazor.Host.Account;
using Blazor.Host.Components;
using Blazor.Host.Shell;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.FluentUI.AspNetCore.Components;
using SharedKernel.Authentication.TokenSigning;
using SharedKernel.Configuration;
using _Imports = Blazor.Client._Imports;
using IPNetwork = System.Net.IPNetwork;

namespace Blazor.Host;

public static class HostApplication
{
    public static WebApplication Build(string[] args, ITokenSigningClient tokenSigningClient)
    {
        // The application name is fixed so the static web assets manifest resolves when another entry assembly builds the host
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ApplicationName = typeof(HostApplication).Assembly.GetName().Name });
        var publicUrl = Uri.TryCreate(Environment.GetEnvironmentVariable(HostShell.PublicUrlKey), UriKind.Absolute, out var parsedPublicUrl)
            ? parsedPublicUrl
            : throw new InvalidOperationException($"{HostShell.PublicUrlKey} is not set to an absolute URL. Start the stack through the AppHost.");

        builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

        builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();

        // Prerendering runs client components on the server, so the server registers the same component services as the client
        builder.Services.AddFluentUIComponents();

        builder.Services.AddSingleton<HostShell>();
        builder.Services.AddHttpContextAccessor();

        // Prerendering an interactive component and the bootstrap endpoint both resolve the contract from this container
        builder.Services.AddScoped<IBootstrapSource, HostBootstrapSource>();

        // The static server-rendered form handlers call the account API directly, the way the gateway reaches it; cookies are forwarded by hand
        var accountApiUrl = Environment.GetEnvironmentVariable("ACCOUNT_API_URL")
                            ?? throw new InvalidOperationException("ACCOUNT_API_URL is not set. Start the stack through the AppHost.");
        builder.Services
            .AddHttpClient<AccountApiClient>(client => client.BaseAddress = new Uri(accountApiUrl))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false });

        // The platform's data protection registration: the APIs' application name locally and the Container Apps key ring in
        // Azure, so an antiforgery token issued here validates at the account API and one issued by the React shell validates here
        builder.Services.AddCrossServiceDataProtection(HostShell.LoadBrandTokens().ProductName);
        builder.Services.AddAntiforgery(options =>
            {
                options.Cookie.Name = HostShell.AntiforgeryCookieName;
                options.Cookie.Path = "/";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.HeaderName = HostShell.AntiforgeryHeaderName;
            }
        );

        builder.Services.AddHostAuthentication(tokenSigningClient);

        var app = builder.Build();

        // The runtime the host runs on, recorded at start because the edition runs on a prerelease framework
        app.Logger.LogInformation("Blazor host running on {FrameworkDescription}", RuntimeInformation.FrameworkDescription);

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", true);
            app.UseHsts();
        }

        app.UseForwardedHeaders(CreateForwardedHeadersOptions(publicUrl));

        app.UsePathBase(AppUrls.PathBase);

        // base-uri 'none' makes the browser ignore <base href>, so the document URL is the base for anything still resolved
        // relatively; /blazor would resolve to the site root, so the path base always gets its trailing slash
        app.Use((context, next) =>
            {
                if (context.Request.Path.HasValue) return next(context);

                context.Response.Redirect($"{context.Request.PathBase}/{context.Request.QueryString}");
                return Task.CompletedTask;
            }
        );
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseRouting();

        // After routing, so the page headers and nonce apply to Razor component endpoints only, including a re-executed not-found page
        var hostShell = app.Services.GetRequiredService<HostShell>();
        app.Use((context, next) => DevelopmentOnlyPages.RejectOutsideDevelopmentAsync(context, next, app.Environment));
        app.Use(hostShell.ApplyPageHeadersAsync);

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        // Temporary bootstrap endpoint: identity, runtime configuration and system-scope feature flags, plus the antiforgery request
        // token the client needs for its API calls. A later task replaces it with the production contract.
        app.MapGet("/api/bootstrap", async (HttpContext context, IBootstrapSource bootstrapSource) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var bootstrap = await bootstrapSource.GetAsync(context.RequestAborted);
                return Results.Json(bootstrap with { Source = "bootstrap-endpoint" });
            }
        );

        // Brand values from platform-settings.jsonc, versioned by content in the URL the host page renders
        app.MapGet(HostShell.BrandStylesheetPath, (HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                return Results.Text(hostShell.BrandStylesheet, "text/css");
            }
        );

        app.MapGet(HostShell.ManifestPath, (HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "no-cache";
                return Results.Text(hostShell.Manifest, "application/manifest+json");
            }
        );

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(_Imports).Assembly);

        return app;
    }

    // Trust as in the platform's AddHttpForwardHeaders: one hop, from loopback (the local gateway) or the Container Apps
    // envoy (100.64.0.0/10) only. X-Forwarded-Host is also honored, because redirects built by the framework would otherwise
    // point at this host's own origin, which the browser cannot reach under the policy's connect-src; it is accepted only
    // when it names the PUBLIC_URL host, so a forged host never becomes the request host or appears in a redirect.
    public static ForwardedHeadersOptions CreateForwardedHeadersOptions(Uri publicUrl)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
            ForwardLimit = 1,
            AllowedHosts = [publicUrl.IdnHost]
        };
        options.KnownIPNetworks.Clear();
        options.KnownIPNetworks.Add(new IPNetwork(IPAddress.Parse("127.0.0.0"), 8));
        options.KnownIPNetworks.Add(new IPNetwork(IPAddress.IPv6Loopback, 128));
        options.KnownIPNetworks.Add(new IPNetwork(IPAddress.Parse("100.64.0.0"), 10));
        options.KnownProxies.Clear();
        return options;
    }
}
