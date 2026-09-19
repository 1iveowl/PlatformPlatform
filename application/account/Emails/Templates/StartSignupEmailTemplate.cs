using Account.Emails.Layout;
using SharedKernel.Emails;
using SharedKernel.Localization;

namespace Account.Emails.Templates;

public sealed record StartSignupEmailTemplate(string Locale, StartSignupEmailModel Data) : EmailTemplateBase(Locale)
{
    public override Type ComponentType => typeof(StartSignupEmail);

    public override object Model => Data;

    public override string RenderPlainText(EmailBrand brand)
    {
        return new EmailPlainTextBuilder()
            .AddParagraph(EmailStrings.ConfirmationCodeBelowHeading)
            .AddParagraph(EmailStrings.EnterCodeInOpenBrowserWindow)
            .AddParagraph(Data.OneTimePassword)
            .AddParagraph(EmailStrings.IgnoreEmailIfNoSignupRequested)
            .WithOneTimePasswordAutofill(Data.Domain, Data.OneTimePassword)
            .Build(brand, Locale);
    }
}

public sealed record StartSignupEmailModel(string OneTimePassword, string Domain, int ExpiryMinutes);
