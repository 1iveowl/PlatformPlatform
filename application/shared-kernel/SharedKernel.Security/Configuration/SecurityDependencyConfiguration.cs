using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Authentication.TokenSigning;

namespace SharedKernel.Configuration;

public static class SecurityDependencyConfiguration
{
    public static ITokenSigningClient GetTokenSigningService()
    {
        if (AzureEnvironment.IsRunningInAzure)
        {
            var keyVaultUri = new Uri(Environment.GetEnvironmentVariable("KEYVAULT_URL")!);
            var keyClient = new KeyClient(keyVaultUri, AzureEnvironment.DefaultAzureCredential);
            var cryptographyClient = new CryptographyClient(
                keyClient.GetKey("authentication-token-signing-key").Value.Id,
                AzureEnvironment.DefaultAzureCredential
            );

            var secretClient = new SecretClient(keyVaultUri, AzureEnvironment.DefaultAzureCredential);
            var issuer = secretClient.GetSecret("authentication-token-issuer").Value.Value;
            var audience = secretClient.GetSecret("authentication-token-audience").Value.Value;

            return new AzureTokenSigningClient(cryptographyClient, issuer, audience);
        }

        return new DevelopmentTokenSigningClient();
    }

    extension(IServiceCollection services)
    {
        public IServiceCollection AddCrossServiceDataProtection(string productName)
        {
            // Configure shared data protection to ensure encrypted data can be shared across all self-contained systems
            var dataProtection = services.AddDataProtection();

            if (!AzureEnvironment.IsRunningInAzure)
            {
                // Set a common application name for all self-contained systems for local development (handled automatically by Azure Container Apps Environment)
                dataProtection.SetApplicationName(productName);
            }

            return services;
        }
    }
}
