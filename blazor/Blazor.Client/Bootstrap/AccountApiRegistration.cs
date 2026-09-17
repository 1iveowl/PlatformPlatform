// The WebAssembly client's account API boundary: one HttpClient per user scope over same-origin calls through the gateway,
// which turns the session cookies into a bearer token, and the typed clients over it.

using Account.Client;

namespace Blazor.Client.Bootstrap;

public static class AccountApiRegistration
{
    extension(IServiceCollection services)
    {
        // A 401 from an API path ends the session in this runtime; every response reports the evaluated feature flags;
        // state-changing calls carry the bootstrap antiforgery token; every call names the UI culture as X-Locale; closest
        // to the network, a logout or tenant switch holds back competing writes and discards what arrives after it ended
        public IServiceCollection AddAccountApiClients(Uri baseAddress, Func<HttpMessageHandler> createPrimaryHandler)
        {
            services.AddScoped<AuthenticationNavigator>();
            services.AddScoped<FeatureFlagState>();
            services.AddScoped<BootstrapAntiforgeryTokenSource>();
            services.AddScoped<SessionTransitionGate>();
            services.AddScoped(serviceProvider =>
                {
                    var localeHeaderHandler = LocaleHeaderHandler.FromCurrentUiCulture();
                    localeHeaderHandler.InnerHandler = new SessionTransitionHandler(
                        serviceProvider.GetRequiredService<SessionTransitionGate>(), serviceProvider.GetRequiredService<AuthenticationNavigator>()
                    )
                    {
                        InnerHandler = createPrimaryHandler()
                    };
                    var unauthorizedResponseHandler = new UnauthorizedResponseHandler(serviceProvider.GetRequiredService<AuthenticationNavigator>())
                    {
                        InnerHandler = new FeatureFlagsHeaderHandler(serviceProvider.GetRequiredService<FeatureFlagState>())
                        {
                            InnerHandler = new AntiforgeryHeaderHandler(serviceProvider.GetRequiredService<BootstrapAntiforgeryTokenSource>())
                            {
                                InnerHandler = localeHeaderHandler
                            }
                        }
                    };
                    return new HttpClient(unauthorizedResponseHandler) { BaseAddress = baseAddress };
                }
            );
            services.AddScoped<EmailAuthenticationClient>();
            services.AddScoped<AuthenticationClient>();
            services.AddScoped<ExternalAuthenticationClient>();
            services.AddScoped<UsersClient>();
            services.AddScoped<TenantsClient>();
            services.AddScoped<FeatureFlagsClient>();
            return services.AddScoped<IBootstrapSource, HttpBootstrapSource>();
        }
    }
}
