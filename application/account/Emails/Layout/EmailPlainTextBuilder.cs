using System.Text;
using SharedKernel.Emails;
using SharedKernel.Localization;

namespace Account.Emails.Layout;

// The plain text twin of an email: the paragraphs of its body, then the same brand and legal footer the layout renders
// in HTML, and for an email carrying a one-time password the autofill line iOS Mail reads from the last line.
public sealed class EmailPlainTextBuilder
{
    private const string FooterSeparator = "----------------------------------------";

    private readonly List<string> _paragraphs = [];
    private string? _oneTimePasswordAutofill;

    // iOS Mail offers a one-tap autofill for a code when the last line of the plain text body names the domain the code
    // belongs to and the code itself. The HTML body carries the same line hidden, for clients that read only HTML.
    public static string OneTimePasswordAutofillLine(string domain, string oneTimePassword)
    {
        return $"@{domain} #{oneTimePassword}";
    }

    public EmailPlainTextBuilder AddParagraph(string text)
    {
        _paragraphs.Add(text);
        return this;
    }

    public EmailPlainTextBuilder AddLinkParagraph(string text, string url)
    {
        return AddParagraph($"{text} {url}");
    }

    public EmailPlainTextBuilder WithOneTimePasswordAutofill(string domain, string oneTimePassword)
    {
        _oneTimePasswordAutofill = OneTimePasswordAutofillLine(domain, oneTimePassword);
        return this;
    }

    public string Build(EmailBrand brand, string locale)
    {
        var blocks = new List<string>(_paragraphs)
        {
            brand.ProductName,
            brand.GetMailTagline(locale),
            FooterSeparator,
            BuildLegalLine(brand)
        };

        if (_oneTimePasswordAutofill is not null) blocks.Add(_oneTimePasswordAutofill);

        return string.Join("\n\n", blocks);
    }

    private static string BuildLegalLine(EmailBrand brand)
    {
        var legalLine = new StringBuilder();
        legalLine.Append(EmailStrings.Privacy).Append(' ').Append(brand.PrivacyUrl);
        legalLine.Append(" · ").Append(EmailStrings.Terms).Append(' ').Append(brand.TermsUrl);
        legalLine.Append(" · ").Append(EmailStrings.DataProcessingAgreement).Append(' ').Append(brand.DataProcessingAgreementUrl);
        legalLine.Append(" · ").Append(EmailStrings.Compliance).Append(' ').Append(brand.ComplianceUrl);
        return legalLine.ToString();
    }
}
