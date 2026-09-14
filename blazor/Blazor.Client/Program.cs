using Blazor.Client.Bootstrap;
using Blazor.Client.Users;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddFluentUIComponents();

// Same-origin calls through the gateway, which turns the session cookies into a bearer token
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<IBootstrapSource, HttpBootstrapSource>();

// The users calls and their page cache, shared by both grid components
builder.Services.AddScoped<UsersApiClient>();

await builder.Build().RunAsync();
