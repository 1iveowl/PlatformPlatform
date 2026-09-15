using Blazor.Client.Bootstrap;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;
using Blazor.Client.Users;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddFluentUIComponents();

builder.Services.AddAccountApiClients(new Uri(builder.HostEnvironment.BaseAddress), () => new HttpClientHandler());

// The server-page cache of every DataList, one per application, and the users surface's mutations
builder.Services.AddScoped<DataListPageCache>();
builder.Services.AddScoped<UsersApiClient>();

// The in-house toast region and the presentation of failed API calls on interactive surfaces
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<ApiFailurePresenter>();

await builder.Build().RunAsync();
