// The values the host reads from platform-settings.jsonc, the settings file embedded in it that the React edition and
// UserInfo read too: the brand tokens and the email suffix that marks a user as internal. Read once at startup.

using System.Text.Json;
using SharedKernel.Localization;

namespace Blazor.Host.Shell;

public sealed record PlatformSettings(BrandTokens Brand, string InternalEmailDomain)
{
    private const string ResourceName = "platform-settings.jsonc";

    public static PlatformSettings Load()
    {
        using var stream = typeof(PlatformSettings).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = document.RootElement;

        return new PlatformSettings(
            BrandTokens.Read(root.GetProperty("branding")),
            root.GetProperty("identity").GetProperty("internalEmailDomain").GetString()!
        );
    }
}

public sealed record BrandTokens(
    string ProductName,
    string ThemeColorLight,
    string ThemeColorDark,
    string BackgroundColor,
    string PrimaryColorLight,
    string PrimaryColorLightForeground,
    string PrimaryColorDark,
    string PrimaryColorDarkForeground,
    // The address the delete-account notice tells a signed-in user to write to; empty when the brand names none
    string SupportEmail,
    // The address the public footer offers to write to; empty when the brand names none
    string ContactEmail,
    // The one-line product description the public footer shows, by culture, from the brand's web tagline map
    IReadOnlyDictionary<string, string> WebTaglines
)
{
    // The tagline of the culture the page renders in, the en-US one when the brand names no tagline for it, or empty
    public string GetTagline(string locale)
    {
        return WebTaglines.GetValueOrDefault(locale) ?? WebTaglines.GetValueOrDefault(SupportedCultures.DefaultLocale) ?? "";
    }

    public static BrandTokens Read(JsonElement branding)
    {
        var themeColor = branding.GetProperty("themeColor");
        var primaryColor = branding.GetProperty("primaryColor");

        return new BrandTokens(
            branding.GetProperty("productName").GetString()!,
            themeColor.GetProperty("light").GetString()!,
            themeColor.GetProperty("dark").GetString()!,
            branding.GetProperty("backgroundColor").GetString()!,
            primaryColor.GetProperty("light").GetString()!,
            primaryColor.GetProperty("lightForeground").GetString()!,
            primaryColor.GetProperty("dark").GetString()!,
            primaryColor.GetProperty("darkForeground").GetString()!,
            branding.GetProperty("supportEmail").GetString() ?? "",
            branding.GetProperty("contactEmail").GetString() ?? "",
            branding.GetProperty("tagline").GetProperty("web").EnumerateObject().ToDictionary(tagline => tagline.Name, tagline => tagline.Value.GetString() ?? "")
        );
    }
}
