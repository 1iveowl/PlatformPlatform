// The back-office API boundary of the WebAssembly client: same-origin calls on the back-office host, which the platform
// authentication in front of that host authenticates. It shares nothing with the app's chain in AccountApiRegistration: no
// bootstrap, no feature flags header, no version write gate and no app login, because a back-office identity is not an app
// session. State-changing calls carry the antiforgery token the host issued for this document, and a 401 sends the browser
// to the platform's login on the back-office host.

using Account.Client;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Blazor.Client.BackOffice;

public static class BackOfficeApiRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddBackOfficeApiClient(Uri baseAddress, Func<HttpMessageHandler> createPrimaryHandler)
        {
            return services.AddScoped(serviceProvider =>
                {
                    var localeHeaderHandler = LocaleHeaderHandler.FromCurrentUiCulture();
                    localeHeaderHandler.InnerHandler = createPrimaryHandler();
                    var unauthorizedHandler = new BackOfficeUnauthorizedHandler(serviceProvider.GetRequiredService<NavigationManager>())
                    {
                        InnerHandler = new AntiforgeryHeaderHandler(new BackOfficeAntiforgeryTokenSource(serviceProvider.GetRequiredService<AntiforgeryStateProvider>()))
                        {
                            InnerHandler = localeHeaderHandler
                        }
                    };
                    return new BackOfficeClient(new HttpClient(unauthorizedHandler) { BaseAddress = baseAddress });
                }
            );
        }
    }
}

// The request token the host issued with this document for the back-office identity it rendered for, carried to the runtime
// by the framework's antiforgery state. Its cookie pair is the host-only __Host-xsrf-token cookie of the back-office host,
// and the account API validates the pair against the same identity, because both processes share the key ring.
public sealed class BackOfficeAntiforgeryTokenSource(AntiforgeryStateProvider antiforgeryStateProvider) : IAntiforgeryTokenSource
{
    public ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(antiforgeryStateProvider.GetAntiforgeryToken()?.Value);
    }
}

// A 401 means the platform's session on the back-office host ended; the browser leaves the runtime once, with a full
// document navigation to the platform's login, which returns to the current page
public sealed class BackOfficeUnauthorizedHandler(NavigationManager navigationManager) : DelegatingHandler
{
    private const string LoginPath = "/.auth/login/aad";

    private bool _isLeaving;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized || _isLeaving) return response;

        _isLeaving = true;
        var returnPath = new Uri(navigationManager.Uri).PathAndQuery;
        navigationManager.NavigateTo($"{LoginPath}?post_login_redirect_uri={Uri.EscapeDataString(returnPath)}", true);
        return response;
    }
}
