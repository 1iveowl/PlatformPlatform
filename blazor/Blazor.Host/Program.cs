// Spike code (Blazor edition, stage B1): the Blazor host behind AppGateway under /blazor/, authenticating the
// gateway's bearer token and serving the page under test with the React shell's headers and substituted values.

using System.Runtime.InteropServices;
using System.Text;
using ApexCharts;
using Blazor.Host.Components;
using Blazor.Host.Shell;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
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
                // An anonymous page request goes to the React login, which returns here after sign-in
                OnChallenge = context =>
                {
                    context.HandleResponse();
                    var request = context.Request;
                    var returnPath = new StringBuilder().Append(request.PathBase).Append(request.Path).Append(request.QueryString).ToString();
                    context.Response.Redirect($"/login?returnPath={Uri.EscapeDataString(returnPath)}");
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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(_Imports).Assembly);

app.Run();
