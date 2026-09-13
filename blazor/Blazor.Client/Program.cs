using ApexCharts;
using Blazor.Client.Bootstrap;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddFluentUIComponents();
builder.Services.AddApexCharts();

// Spike (B2): same-origin calls through the gateway, which turns the session cookies into a bearer token
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<IBootstrapSource, HttpBootstrapSource>();

await builder.Build().RunAsync();
