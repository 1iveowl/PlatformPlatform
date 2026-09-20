using System.Buffers.Text;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace AppHost;

public static class SecretManagerHelper
{
    private static readonly IConfigurationRoot ConfigurationRoot = new ConfigurationBuilder().AddUserSecrets(UserSecretsId).Build();

    private static string UserSecretsId => Assembly.GetEntryAssembly()!.GetCustomAttribute<UserSecretsIdAttribute>()!.UserSecretsId;

    public static void GenerateAuthenticationTokenSigningKey(string secretName)
    {
        if (string.IsNullOrEmpty(ConfigurationRoot[secretName]))
        {
            var key = new byte[64]; // 512-bit key
            RandomNumberGenerator.Fill(key);
            var base64Key = Convert.ToBase64String(key);
            SaveSecrectToDotNetUserSecrets(secretName, base64Key);
        }
    }

    /// <summary>
    ///     The VAPID key pair the account API signs Web Push requests with, as the base64url values the protocol uses:
    ///     the public key is the uncompressed P-256 point a browser subscribes with, the private key its scalar. A
    ///     development pair is generated once and kept in user secrets, the way the token signing key is, so push
    ///     notifications work locally without anyone being asked for a key. An operator who wants their own pair sets
    ///     both secrets and restarts.
    /// </summary>
    public static (string PublicKey, string PrivateKey) GenerateWebPushVapidKeyPair(string publicKeySecretName, string privateKeySecretName)
    {
        var publicKey = ConfigurationRoot[publicKeySecretName];
        var privateKey = ConfigurationRoot[privateKeySecretName];
        if (!string.IsNullOrEmpty(publicKey) && !string.IsNullOrEmpty(privateKey)) return (publicKey, privateKey);

        using var keyPair = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = keyPair.ExportParameters(true);
        publicKey = Base64Url.EncodeToString([0x04, .. parameters.Q.X!, .. parameters.Q.Y!]);
        privateKey = Base64Url.EncodeToString(parameters.D!);

        SaveSecrectToDotNetUserSecrets(publicKeySecretName, publicKey);
        SaveSecrectToDotNetUserSecrets(privateKeySecretName, privateKey);

        return (publicKey, privateKey);
    }

    private static void SaveSecrectToDotNetUserSecrets(string key, string value)
    {
        var args = $"user-secrets set {key} {value} --id {UserSecretsId}";
        var startInfo = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
    }

    extension(IDistributedApplicationBuilder builder)
    {
        public IResourceBuilder<ParameterResource> CreateStablePassword(string secretName)
        {
            var password = ConfigurationRoot[secretName];

            if (string.IsNullOrEmpty(password))
            {
                var passwordGenerator = new GenerateParameterDefault
                {
                    MinLower = 5, MinUpper = 5, MinNumeric = 3, MinSpecial = 3
                };
                password = passwordGenerator.GetDefaultValue();
                SaveSecrectToDotNetUserSecrets(secretName, password);
            }

            return builder.CreateResourceBuilder(new ParameterResource(secretName, _ => password, true));
        }
    }
}
