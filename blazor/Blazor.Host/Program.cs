// The Blazor host behind AppGateway under the path base in AppUrls. It authenticates the gateway's bearer token, serves
// the static server-rendered public surface and the prerendered WebAssembly pages with the React shell's headers, and
// serves the brand stylesheet, the web app manifest and the temporary bootstrap endpoint.

using System.Runtime.InteropServices;
using System.Text;
using Blazor.Client;
using Blazor.Client.Bootstrap;
using Blazor.Host.Account;
using Blazor.Host.Components;
using Blazor.Host.Shell;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.IdentityModel.Tokens;
using _Imports = Blazor.Client._Imports;

// Development parameters of SharedKernel's DevelopmentTokenSigningClient: issuer and audience "Localhost" and an HMAC
// key from the shared user secrets store that the AppHost writes on start
const string developmentTokenIssuerAndAudience = "Localhost";
const string sharedUserSecretsId = "platformplatform-f817f2a1-ac57-4756-aef2-a57ca864bbd3";
const string tokenSigningKeySecretName = "authentication-token-signing-key";

var builder = WebApplication.CreateBuilder(args);

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

// Same application name as SharedKernel's AddCrossServiceDataProtection locally, so the antiforgery cookie issued here
// and the one issued by the React shell are readable by both
builder.Services.AddDataProtection().SetApplicationName(HostShell.LoadBrandTokens().ProductName);
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

var signingKey = new ConfigurationBuilder().AddUserSecrets(sharedUserSecretsId).Build()[tokenSigningKeySecretName]
                 ?? throw new InvalidOperationException($"User secret '{tokenSigningKeySecretName}' is not configured. Start the stack through the AppHost first.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = developmentTokenIssuerAndAudience,
                ValidateAudience = true,
                ValidAudience = developmentTokenIssuerAndAudience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(signingKey)),
                ClockSkew = TimeSpan.FromSeconds(5),
                ValidateLifetime = true
            };
            options.Events = new JwtBearerEvents
            {
                // An anonymous request for an authenticated page goes to the Blazor login, which returns here after sign-in
                OnChallenge = context =>
                {
                    context.HandleResponse();
                    var request = context.Request;
                    var returnPath = new StringBuilder().Append(request.PathBase).Append(request.Path).Append(request.QueryString).ToString();
                    context.Response.Redirect($"{request.PathBase}/login?returnPath={Uri.EscapeDataString(returnPath)}");
                    return Task.CompletedTask;
                }
            };
        }
    );
builder.Services.AddAuthorization();

var app = builder.Build();

// The runtime the host runs on, recorded at start because the edition runs on a prerelease framework
app.Logger.LogInformation("Blazor host running on {FrameworkDescription}", RuntimeInformation.FrameworkDescription);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", true);
    app.UseHsts();
}

// YARP sends X-Forwarded-Host and X-Forwarded-Proto; without them NavigationManager builds redirect URLs on this
// host's own Kestrel origin (https://localhost:<port>), which the browser cannot follow under the policy's connect-src
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
};
app.UseForwardedHeaders(forwardedHeadersOptions);

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

app.Run();
