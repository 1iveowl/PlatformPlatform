using Account.Emails.Layout;
using SharedKernel.Emails;
using SharedKernel.Localization;

namespace Account.Emails.Templates;

public sealed record StartLoginEmailTemplate(string Locale, StartLoginEmailModel Data) : EmailTemplateBase(Locale)
{
    public override Type ComponentType => typeof(StartLoginEmail);

    public override object Model => Data;

    public override string RenderPlainText(EmailBrand brand)
    {
        return new EmailPlainTextBuilder()
            .AddParagraph(EmailStrings.ConfirmationCodeBelowHeading)
            .AddParagraph(EmailStrings.EnterCodeInOpenBrowserWindow)
            .AddParagraph(Data.OneTimePassword)
            .AddParagraph(EmailStrings.IgnoreEmailIfNoLoginAttempt)
            .WithOneTimePasswordAutofill(Data.Domain, Data.OneTimePassword)
            .Build(brand, Locale);
    }
}

public sealed record StartLoginEmailModel(string OneTimePassword, string Domain, int ExpiryMinutes);
