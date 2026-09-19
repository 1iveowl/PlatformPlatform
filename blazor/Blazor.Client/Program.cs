using Blazor.Client.Bootstrap;
using Blazor.Client.Components;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;
using Blazor.Client.Localization;
using Blazor.Client.Preferences;
using Blazor.Client.Session;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddLocalization();
builder.Services.AddFluentUIComponents(configuration => configuration.Localizer = new FluentResourceLocalizer());

builder.Services.AddAccountApiClients(new Uri(builder.HostEnvironment.BaseAddress), () => new HttpClientHandler());

// The server-page cache of every DataList, one per application
builder.Services.AddScoped<DataListPageCache>();

// The viewport width for the components that change the DOM with it, such as a side pane; one set of media listeners per runtime
builder.Services.AddScoped<ViewportState>();

// The in-house toast region and the presentation of failed API calls on interactive surfaces
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<ApiFailurePresenter>();

// The signed-in user's bootstrap shared by the header and the page component, torn down when the surface is left
builder.Services.AddScoped<SessionState>();

// Watches whether the publish this runtime was loaded from is still served; one per application, so the asset route it
// captured survives the enhanced navigations that replace the document
builder.Services.AddScoped<StaleAssetProbe>();

// Logout and tenant switch, one at a time, for whichever component offers them
builder.Services.AddScoped<SessionTransition>();

// Ends this runtime's identity when another tab of the browser logs out, logs in or switches tenant
builder.Services.AddScoped<AuthSyncCoordinator>();
builder.Services.AddScoped<DevicePreferences>();

// The language of the signed-in user, one change at a time, shared by the preferences page and the shell's mobile menu
builder.Services.AddScoped<LocaleSwitch>();

var host = builder.Build();

// Before RunAsync, which loads the satellite resources of the current culture and renders the root components
ClientCulture.Apply((IJSInProcessRuntime)host.Services.GetRequiredService<IJSRuntime>());

await host.RunAsync();
