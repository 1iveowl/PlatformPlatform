using Account.Emails.Layout;
using SharedKernel.Emails;
using SharedKernel.Localization;

namespace Account.Emails.Templates;

public sealed record ResendEmailLoginEmailTemplate(string Locale, ResendEmailLoginEmailModel Data) : EmailTemplateBase(Locale)
{
    public override Type ComponentType => typeof(ResendEmailLoginEmail);

    public override object Model => Data;

    public override string RenderPlainText(EmailBrand brand)
    {
        return new EmailPlainTextBuilder()
            .AddParagraph(EmailStrings.NewVerificationCodeHeading)
            .AddParagraph(EmailStrings.SendingCodeAgainAsRequested)
            .AddParagraph(Data.OneTimePassword)
            .AddParagraph(EmailStrings.CodeExpiresInAFewMinutes)
            .AddParagraph(EmailStrings.IgnoreEmailIfNoNewCodeRequested)
            .WithOneTimePasswordAutofill(Data.Domain, Data.OneTimePassword)
            .Build(brand, Locale);
    }
}

public sealed record ResendEmailLoginEmailModel(string OneTimePassword, string Domain, int ExpiryMinutes);
