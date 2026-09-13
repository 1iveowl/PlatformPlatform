// Spike code (Blazor edition, stages B1 and B2): the Blazor host behind AppGateway under /blazor/, authenticating the
// gateway's bearer token and serving the page under test with the React shell's headers and substituted values.
// B2 adds the static SSR public surface with the email login and signup forms, and the throwaway bootstrap endpoint.

using System.Runtime.InteropServices;
using System.Text;
using ApexCharts;
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
const string pathBase = "/blazor";

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();

// Prerendering runs client components on the server, so the server registers the same component services as the client
builder.Services.AddFluentUIComponents();
builder.Services.AddApexCharts();

builder.Services.AddSingleton<HostShell>();
builder.Services.AddHttpContextAccessor();

// B2: prerendering an interactive component and the bootstrap endpoint both resolve the contract from this container
builder.Services.AddScoped<IBootstrapSource, HostBootstrapSource>();

// B2: static SSR form handlers call the account API directly, the way the gateway reaches it; cookies are forwarded by hand
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
                // B2: an anonymous request for an authenticated page goes to the Blazor login, which returns here after sign-in
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

// Launch evidence for B1: the runtime the host actually runs on
app.Logger.LogInformation("Blazor host running on {FrameworkDescription}", RuntimeInformation.FrameworkDescription);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", true);
    app.UseHsts();
}

// B2: YARP sends X-Forwarded-Host and X-Forwarded-Proto; without them NavigationManager builds redirect URLs on this
// host's own Kestrel origin (https://localhost:<port>), which the browser cannot follow under the policy's connect-src
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
};
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UsePathBase(pathBase);

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
app.Use(hostShell.ApplyPageHeadersAsync);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// B2: throwaway bootstrap endpoint demonstrating the contract stage C session C3 builds: identity, runtime configuration and
// system-scope feature flags, plus the antiforgery request token the client needs for its API calls
app.MapGet("/api/bootstrap", async (HttpContext context, IBootstrapSource bootstrapSource) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        var bootstrap = await bootstrapSource.GetAsync(context.RequestAborted);
        return Results.Json(bootstrap with { Source = "bootstrap-endpoint" });
    }
);

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(_Imports).Assembly);

app.Run();
