using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Authentication.BackOfficeIdentity;

namespace Account.Tests.BackOffice;

// The test server receives every request without a listener port, so the back-office identity handler would refuse all
// principal headers. Locally the back-office host is served only on the back-office listener, so this gives each request
// naming the back-office host that listener's port and configures the port the handler compares against. It lives in the
// test host only; Development and Azure configuration have no equivalent.
public static class BackOfficeListenerSimulation
{
    private const int SimulatedBackOfficeListenerPort = 49001;

    extension(IWebHostBuilder builder)
    {
        public IWebHostBuilder SimulateBackOfficeListener(string backOfficeHost)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { [BackOfficeListener.LocalPortKey] = SimulatedBackOfficeListenerPort.ToString() }
                )
            );

            return builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new BackOfficeListenerStartupFilter(backOfficeHost)));
        }
    }

    private sealed class BackOfficeListenerStartupFilter(string backOfficeHost) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use((context, nextMiddleware) =>
                    {
                        if (string.Equals(context.Request.Host.Host, backOfficeHost, StringComparison.OrdinalIgnoreCase))
                        {
                            context.Connection.LocalPort = SimulatedBackOfficeListenerPort;
                        }

                        return nextMiddleware(context);
                    }
                );
                next(app);
            };
        }
    }
}
