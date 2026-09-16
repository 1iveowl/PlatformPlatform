using Blazor.Client.Bootstrap;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;
using Blazor.Client.Localization;
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

// The in-house toast region and the presentation of failed API calls on interactive surfaces
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<ApiFailurePresenter>();

// The signed-in user's bootstrap shared by the header and the page component, torn down when the surface is left
builder.Services.AddScoped<SessionState>();

var host = builder.Build();

// Before RunAsync, which loads the satellite resources of the current culture and renders the root components
ClientCulture.Apply((IJSInProcessRuntime)host.Services.GetRequiredService<IJSRuntime>());

await host.RunAsync();
