using Microsoft.Extensions.DependencyInjection;
using SharedKernel.SinglePageApp;

namespace SharedKernel.Emails;

public static class EmailRenderingConfiguration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddEmailRendering()
        {
            var publicUrl = Environment.GetEnvironmentVariable(SinglePageAppConfiguration.PublicUrlKey) ?? string.Empty;

            services.AddSingleton(new EmailBrand(publicUrl));
            services.AddSingleton<IEmailRenderer, RazorEmailRenderer>();

            return services;
        }
    }
}
