using SharedKernel.Platform;

namespace SharedKernel.Emails;

// The brand values every transactional email renders: the product name and header colour from platform-settings.jsonc,
// the per-locale mail tagline, and the absolute URLs of the logo and the legal pages. The URLs are absolute because an
// email is read outside the application, and they are built from the running deploy's public URL so the same components
// work on localhost, in staging and in production.
public sealed class EmailBrand(string publicUrl)
{
    public string PublicUrl { get; } = publicUrl.TrimEnd('/');

    public string ProductName => Settings.Current.Branding.ProductName;

    public string HeaderBackground => Settings.Current.Branding.EmailHeaderBackground;

    public string LogoUrl => $"{PublicUrl}/email/logo-1200x184.png";

    public string PrivacyUrl => $"{PublicUrl}/legal/privacy";

    public string TermsUrl => $"{PublicUrl}/legal/terms";

    public string DataProcessingAgreementUrl => $"{PublicUrl}/legal/dpa";

    public string ComplianceUrl => $"{PublicUrl}/legal";

    public string GetMailTagline(string locale)
    {
        var mailTaglines = Settings.Current.Branding.Tagline.Mail;
        if (mailTaglines.TryGetValue(locale, out var tagline)) return tagline;

        throw new InvalidOperationException(
            $"platform-settings.jsonc: branding.tagline.mail does not include locale '{locale}'. Available: [{string.Join(", ", mailTaglines.Keys)}]."
        );
    }
}
