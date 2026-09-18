// The Blazor host behind AppGateway under the path base in AppUrls. It authenticates the gateway's bearer token, serves
// the static server-rendered public surface and the prerendered WebAssembly pages with the React shell's headers, and
// serves the brand stylesheet and the web app manifest. Clients read the bootstrap contract from the account API.

using System.Net;
using System.Runtime.InteropServices;
using Account.Client;
using Blazor.Client;
using Blazor.Client.Bootstrap;
using Blazor.Client.Components;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;
using Blazor.Client.Localization;
using Blazor.Client.Preferences;
using Blazor.Client.Session;
using Blazor.Host.Account;
using Blazor.Host.Components;
using Blazor.Host.Shell;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.FluentUI.AspNetCore.Components;
using SharedKernel.Authentication.TokenSigning;
using SharedKernel.Configuration;
using SharedKernel.Localization;
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
        builder.Services.AddLocalization();
        builder.Services.AddFluentUIComponents(configuration => configuration.Localizer = new FluentResourceLocalizer());

        builder.Services.AddSingleton<HostShell>();
        // Reads and renders the three legal documents once, so a document outside the renderer's policy stops the host here
        builder.Services.AddSingleton<LegalDocuments>();
        builder.Services.AddHttpContextAccessor();

        // Prerendering an interactive component reads the bootstrap contract through the host's adapter; the components'
        // navigation out of the authenticated surface and their toast presentation of failed calls resolve from the same container
        builder.Services.AddScoped<IBootstrapSource, HostBootstrapSource>();
        builder.Services.AddScoped<AuthenticationNavigator>();
        builder.Services.AddScoped<ToastService>();
        builder.Services.AddScoped<ApiFailurePresenter>();
        builder.Services.AddScoped<DataListPageCache>();
        builder.Services.AddScoped<ViewportState>();
        builder.Services.AddScoped<SessionState>();
        builder.Services.AddScoped<DevicePreferences>();

        // The static server-rendered form handlers call the account API directly through the typed clients, the way the gateway
        // reaches it; HostAccountApiHandler relays the current request's credentials by hand. The feature flag state is
        // registered so prerendered components resolve the same services as in the browser.
        var accountApiUrl = new Uri(Environment.GetEnvironmentVariable("ACCOUNT_API_URL")
                                    ?? throw new InvalidOperationException("ACCOUNT_API_URL is not set. Start the stack through the AppHost.")
        );
        builder.Services.AddTransient<HostAccountApiHandler>();
        builder.Services.AddScoped<FeatureFlagState>();
        AddAccountApiClient<EmailAuthenticationClient>(builder.Services, accountApiUrl);
        AddAccountApiClient<AuthenticationClient>(builder.Services, accountApiUrl);
        AddAccountApiClient<ExternalAuthenticationClient>(builder.Services, accountApiUrl);
        AddAccountApiClient<UsersClient>(builder.Services, accountApiUrl);
        AddAccountApiClient<TenantsClient>(builder.Services, accountApiUrl);
        AddAccountApiClient<FeatureFlagsClient>(builder.Services, accountApiUrl);

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

        // In every environment, so the error page renders inside the shell in Development too; the page reveals the exception
        // message and stack in Development only
        app.UseExceptionHandler("/Error", true);
        if (!app.Environment.IsDevelopment())
        {
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
        app.Use(HostShell.RewriteLinkHeadersAsync);
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseRouting();

        // After routing, so the page headers and nonce apply to Razor component endpoints only, including a re-executed not-found page
        var hostShell = app.Services.GetRequiredService<HostShell>();
        app.Use((context, next) => DevelopmentOnlyPages.RejectOutsideDevelopmentAsync(context, next, app.Environment));
        app.Use(hostShell.ApplyPageHeadersAsync);

        app.UseAuthentication();
        // After authentication, so a signed-in user's locale claim decides; the culture is set for this request only
        app.UseRequestLocalization(CreateRequestLocalizationOptions());
        app.UseAuthorization();
        app.UseAntiforgery();

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

    // The platform's selection order in HostShell.GetLocale (the locale claim, the preferred-locale cookie, Accept-Language,
    // en-US) is the only provider, so the query string, the framework's culture cookie and the framework's own
    // Accept-Language matching never pick a culture
    public static RequestLocalizationOptions CreateRequestLocalizationOptions()
    {
        var options = new RequestLocalizationOptions()
            .AddSupportedCultures(SupportedCultures.Locales)
            .AddSupportedUICultures(SupportedCultures.Locales)
            .SetDefaultCulture(SupportedCultures.DefaultLocale);
        options.RequestCultureProviders.Clear();
        options.RequestCultureProviders.Add(new CustomRequestCultureProvider(context => Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(HostShell.GetLocale(context))))
        );
        return options;
    }

    private static void AddAccountApiClient<TClient>(IServiceCollection services, Uri accountApiUrl) where TClient : class
    {
        services
            .AddHttpClient<TClient>(client => client.BaseAddress = accountApiUrl)
            .AddHttpMessageHandler<HostAccountApiHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false });
    }
}
