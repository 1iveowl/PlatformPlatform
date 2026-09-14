using System.Text.Json;
using Azure.Security.KeyVault.Secrets;
using FluentValidation;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.ApiResults;
using SharedKernel.Authentication;
using SharedKernel.Authentication.TokenGeneration;
using SharedKernel.Authentication.TokenSigning;
using SharedKernel.DomainEvents;
using SharedKernel.Integrations.Email;
using SharedKernel.Persistence;
using SharedKernel.PipelineBehaviors;
using SharedKernel.Platform;
using SharedKernel.Telemetry;

namespace SharedKernel.Configuration;

public static class SharedDependencyConfiguration
{
    // Ensure that enums are serialized as strings and use CamelCase, with the same options the clients use
    public static readonly JsonSerializerOptions DefaultJsonSerializerOptions = ApiJsonSerializerOptions.Create();

    public static ITokenSigningClient GetTokenSigningService()
    {
        return SecurityDependencyConfiguration.GetTokenSigningService();
    }

    extension(IServiceCollection services)
    {
        public IServiceCollection AddSharedServices<T>(Assembly[] assemblies)
            where T : DbContext
        {
            // Even though the HttpContextAccessor is not available in Worker Services, it is still registered here because
            // workers register the same CommandHandlers as the API, which may require the HttpContext.
            // Consider making a generic IRequestContextProvider that can return the HttpContext only if it is available.
            services.AddHttpContextAccessor();

            return services
                .AddServiceDiscovery()
                .AddSingleton(GetTokenSigningService())
                .AddCrossServiceDataProtection(Settings.Current.Branding.ProductName)
                .AddSingleton(Settings.Current)
                .AddTimeProvider()
                .AddAuthentication()
                .AddDefaultJsonSerializerOptions()
                .AddPersistenceHelpers<T>()
                .AddDefaultHealthChecks()
                .AddEmailClient()
                .AddMediatRPipelineBehaviors()
                .RegisterMediatRRequest(assemblies)
                .RegisterRepositories(assemblies);
        }

        private IServiceCollection AddTimeProvider()
        {
            services.TryAddSingleton(TimeProvider.System); // Use Try to allow tests to override with a fake TimeProvider
            return services;
        }

        private IServiceCollection AddAuthentication()
        {
            return services
                .AddScoped<IPasswordHasher<object>, PasswordHasher<object>>()
                .AddScoped<OneTimePasswordHelper>()
                .AddScoped<RefreshTokenGenerator>()
                .AddScoped<AccessTokenGenerator>()
                .AddScoped<AuthenticationTokenService>();
        }

        private IServiceCollection AddDefaultJsonSerializerOptions()
        {
            return services.Configure<JsonOptions>(options =>
                {
                    // Copy the default options from the DefaultJsonSerializerOptions to enforce consistency in serialization.
                    foreach (var jsonConverter in DefaultJsonSerializerOptions.Converters)
                    {
                        options.SerializerOptions.Converters.Add(jsonConverter);
                    }

                    options.SerializerOptions.PropertyNamingPolicy = DefaultJsonSerializerOptions.PropertyNamingPolicy;
                }
            );
        }

        private IServiceCollection AddPersistenceHelpers<T>() where T : DbContext
        {
            return services
                .AddScoped<IUnitOfWork, UnitOfWork>(provider => new UnitOfWork(provider.GetRequiredService<T>()))
                .AddScoped<IDomainEventCollector, DomainEventCollector>(provider =>
                    new DomainEventCollector(provider.GetRequiredService<T>())
                );
        }

        private IServiceCollection AddDefaultHealthChecks()
        {
            // Add a default liveness check to ensure the app is responsive
            services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
            return services;
        }

        private IServiceCollection AddEmailClient()
        {
            if (SharedInfrastructureConfiguration.IsRunningInAzure)
            {
                var keyVaultUri = new Uri(Environment.GetEnvironmentVariable("KEYVAULT_URL")!);
                services
                    .AddSingleton(_ => new SecretClient(keyVaultUri, SharedInfrastructureConfiguration.DefaultAzureCredential))
                    .AddTransient<IEmailClient, AzureEmailClient>();
            }
            else
            {
                services
                    .AddSingleton(_ => PortAllocation.Load())
                    .AddTransient<IEmailClient, DevelopmentEmailClient>();
            }

            return services;
        }

        private IServiceCollection AddMediatRPipelineBehaviors()
        {
            // Order is important! First all Pre behaviors run, then the command is handled, and finally all Post behaviors run.
            // So Validation → Command → PublishDomainEvents → UnitOfWork → PublishTelemetryEvents.
            services
                .AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>)) // Pre
                .AddTransient(typeof(IPipelineBehavior<,>), typeof(PublishTelemetryEventsPipelineBehavior<,>)) // Post
                .AddTransient(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkPipelineBehavior<,>)) // Post
                .AddTransient(typeof(IPipelineBehavior<,>), typeof(PublishDomainEventsPipelineBehavior<,>)); // Post

            return services
                .AddScoped<ITelemetryEventsCollector, TelemetryEventsCollector>()
                .AddScoped<ConcurrentCommandCounter>();
        }

        private IServiceCollection RegisterMediatRRequest(Assembly[] assemblies)
        {
            return services
                .AddMediatR(configuration => configuration.RegisterServicesFromAssemblies(assemblies))
                .AddValidatorsFromAssemblies(assemblies);
        }

        private IServiceCollection RegisterRepositories(Assembly[] assemblies)
        {
            // Scrutor will scan the assembly for all classes that implement the IRepository
            // and register them as a service in the container.
            return services
                .Scan(scan => scan
                    .FromAssemblies(assemblies)
                    .AddClasses(classes => classes.Where(type =>
                            type.BaseType is { IsGenericType: true } &&
                            (type.BaseType.GetGenericTypeDefinition() == typeof(RepositoryBase<,>) ||
                             type.BaseType.GetGenericTypeDefinition() == typeof(SoftDeletableRepositoryBase<,>))
                        ), false
                    )
                    .AsImplementedInterfaces()
                    .WithScopedLifetime()
                );
        }
    }
}
