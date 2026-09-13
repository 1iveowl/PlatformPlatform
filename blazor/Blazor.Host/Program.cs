using ApexCharts;
using Blazor.Host.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using _Imports = Blazor.Client._Imports;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();

// Prerendering runs client components on the server, so the server registers the same component services as the client
builder.Services.AddFluentUIComponents();
builder.Services.AddApexCharts();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(_Imports).Assembly);

app.Run();
