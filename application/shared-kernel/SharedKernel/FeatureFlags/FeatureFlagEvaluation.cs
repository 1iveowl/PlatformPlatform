using Microsoft.Extensions.Configuration;

namespace SharedKernel.FeatureFlags;

// Server-side evaluation of the portable feature flag declarations in SharedKernel.Contracts.
public static class FeatureFlagEvaluation
{
    extension(FeatureFlagDefinition featureFlag)
    {
        public bool IsSystemFeatureFlagEnabled(IConfiguration configuration)
        {
            if (featureFlag.Scope != FeatureFlagScope.System || featureFlag.SystemConfigKey is null) return false;

            var configValue = configuration[featureFlag.SystemConfigKey];

            return featureFlag.SystemConfigExpectedValue is not null
                ? configValue == featureFlag.SystemConfigExpectedValue
                : !string.IsNullOrEmpty(configValue);
        }
    }
}
