using Account.Emails.Layout;
using SharedKernel.Emails;
using SharedKernel.Localization;

namespace Account.Emails.Templates;

public sealed record InviteUserEmailTemplate(string Locale, InviteUserEmailModel Data) : EmailTemplateBase(Locale)
{
    public override Type ComponentType => typeof(InviteUserEmail);

    public override object Model => Data;

    public override string RenderPlainText(EmailBrand brand)
    {
        var linkText = $"{EmailStrings.GoToThisPageInYourOpenBrowser} {Data.LoginUrl}";

        return new EmailPlainTextBuilder()
            .AddParagraph(EmailText.Format(EmailStrings.InvitedYouToJoinHeading, Data.InviterName, brand.ProductName))
            .AddParagraph(EmailText.Format(EmailStrings.GainAccessInstruction, linkText, Data.Email))
            .AddParagraph(EmailStrings.IgnoreEmailIfSenderUnknown)
            .Build(brand, Locale);
    }
}

public sealed record InviteUserEmailModel(string InviterName, string TenantName, string Email, string LoginUrl);
