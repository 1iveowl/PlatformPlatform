using Blazor.Client.Bootstrap;
using Blazor.Client.Users;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddFluentUIComponents();

// Same-origin calls through the gateway, which turns the session cookies into a bearer token; a 401 from an API path
// ends the session in this runtime
builder.Services.AddScoped<AuthenticationNavigator>();
builder.Services.AddScoped(serviceProvider =>
    {
        var unauthorizedResponseHandler = new UnauthorizedResponseHandler(serviceProvider.GetRequiredService<AuthenticationNavigator>())
        {
            InnerHandler = new HttpClientHandler()
        };
        return new HttpClient(unauthorizedResponseHandler) { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
    }
);
builder.Services.AddScoped<IBootstrapSource, HttpBootstrapSource>();

// The users calls and their page cache, shared by both grid components
builder.Services.AddScoped<UsersApiClient>();

await builder.Build().RunAsync();
