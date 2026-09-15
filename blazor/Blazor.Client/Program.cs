using Blazor.Client.Bootstrap;
using Blazor.Client.Forms;
using Blazor.Client.Users;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddFluentUIComponents();

builder.Services.AddAccountApiClients(new Uri(builder.HostEnvironment.BaseAddress), () => new HttpClientHandler());

// The users calls and their page cache, shared by both grid components
builder.Services.AddScoped<UsersApiClient>();

// The in-house toast region and the presentation of failed API calls on interactive surfaces
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<ApiFailurePresenter>();

await builder.Build().RunAsync();
