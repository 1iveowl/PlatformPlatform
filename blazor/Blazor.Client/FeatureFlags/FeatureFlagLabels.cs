// The localized name and description of a feature flag. The registry carries an English label and description for the back
// office and, through the generated manifest, for the React client; the Blazor edition shows the same text from
// AccountStrings, by the convention FeatureFlag<PascalCaseKey>Name and FeatureFlag<PascalCaseKey>Description.
// FeatureFlagLabelsTests fails when a configurable flag in the registry has no such pair in either culture, so adding a
// configurable flag without its two strings cannot pass. The registry text is the fallback, in the language it is written
// in, so a flag that reaches the client before its strings do still renders readably.

using System.Globalization;
using FeatureFlagRegistry = SharedKernel.FeatureFlags.FeatureFlags;

namespace Blazor.Client.FeatureFlags;

public static class FeatureFlagLabels
{
    public const string NameSuffix = "Name";

    public const string DescriptionSuffix = "Description";

    // Null for a key the registry does not declare, so an unknown key from the API renders no switch instead of a raw key
    public static FeatureFlagRow? TryCreateRow(string flagKey, bool enabled)
    {
        var definition = FeatureFlagRegistry.Get(flagKey);
        return definition is null
            ? null
            : new FeatureFlagRow(definition.Key, ResourceOrDefault(definition.Key, NameSuffix, definition.Label), ResourceOrDefault(definition.Key, DescriptionSuffix, definition.Description), enabled);
    }

    public static string ResourceName(string flagKey, string suffix)
    {
        var words = flagKey.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(word => string.Concat(word[..1].ToUpperInvariant(), word[1..]));
        return $"FeatureFlag{string.Concat(words)}{suffix}";
    }

    // The entry for the current UI culture, or null when the key has no entry at all
    public static string? TryResource(string flagKey, string suffix)
    {
        return AccountStrings.ResourceManager.GetString(ResourceName(flagKey, suffix), CultureInfo.CurrentUICulture);
    }

    private static string ResourceOrDefault(string flagKey, string suffix, string fallback)
    {
        return TryResource(flagKey, suffix) ?? fallback;
    }
}
