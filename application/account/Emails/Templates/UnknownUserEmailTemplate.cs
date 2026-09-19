using Account.Emails.Layout;
using SharedKernel.Emails;
using SharedKernel.Localization;

namespace Account.Emails.Templates;

public sealed record UnknownUserEmailTemplate(string Locale, UnknownUserEmailModel Data) : EmailTemplateBase(Locale)
{
    public override Type ComponentType => typeof(UnknownUserEmail);

    public override object Model => Data;

    public override string RenderPlainText(EmailBrand brand)
    {
        return new EmailPlainTextBuilder()
            .AddParagraph(EmailStrings.RightEmailAddressHeading)
            .AddParagraph(EmailText.Format(EmailStrings.NoAccountTiedToEmail, brand.ProductName, Data.Email))
            .AddParagraph(EmailStrings.TryAnotherEmailOrSignUp)
            .AddLinkParagraph(EmailStrings.SignUpForAnAccount, Data.SignupUrl)
            .AddParagraph(EmailStrings.NoAccountWasCreated)
            .Build(brand, Locale);
    }
}

public sealed record UnknownUserEmailModel(string Email, string SignupUrl);
