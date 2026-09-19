using Account.Emails.Templates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Emails;
using SharedKernel.Localization;

namespace Account.Emails;

// The back-office component gallery shows each transactional email in an iframe pointed at
// /emails/assets/<Template>.<culture>.preview.html. The page is rendered on the spot from the same components the
// backend mails, with sample data, so what a designer sees is what a recipient gets.
//
// The preview path is served on the auth-gated back-office host only and answers 404 on the user-facing host, so a
// guessed path never reaches it.
public static class EmailPreviews
{
    public const string RequestPath = "/emails/assets";

    private const string PreviewSuffix = ".preview.html";

    private const string SampleOneTimePassword = "ABC123";
    private const string SampleDomain = "app.example.com";
    private const string SampleInviterName = "Alex Taylor";
    private const string SampleTenantName = "Contoso";
    private const string SampleInviteeEmail = "invitee@example.com";
    private const string SampleUnknownEmail = "alex@example.com";

    // The names are the ones the back-office gallery asks for, kept short so the page's own list reads as the email does
    public static EmailTemplateBase? CreateSample(string templateName, string locale, EmailBrand brand)
    {
        return templateName switch
        {
            "StartLogin" => new StartLoginEmailTemplate(locale, new StartLoginEmailModel(SampleOneTimePassword, SampleDomain, 5)),
            "StartSignup" => new StartSignupEmailTemplate(locale, new StartSignupEmailModel(SampleOneTimePassword, SampleDomain, 5)),
            "ResendEmailLogin" => new ResendEmailLoginEmailTemplate(locale, new ResendEmailLoginEmailModel(SampleOneTimePassword, SampleDomain, 5)),
            "UnknownUser" => new UnknownUserEmailTemplate(locale, new UnknownUserEmailModel(SampleUnknownEmail, $"{brand.PublicUrl}/signup")),
            "InviteUser" => new InviteUserEmailTemplate(locale, new InviteUserEmailModel(SampleInviterName, SampleTenantName, SampleInviteeEmail, $"{brand.PublicUrl}/login")),
            _ => null
        };
    }

    // A preview request is /emails/assets/<Template>.<culture>.preview.html, and nothing else on that path.
    private static bool TryReadPreviewRequest(string? path, out string templateName, out string locale)
    {
        templateName = string.Empty;
        locale = string.Empty;

        if (path is null || !path.StartsWith($"{RequestPath}/", StringComparison.OrdinalIgnoreCase)) return false;
        if (!path.EndsWith(PreviewSuffix, StringComparison.OrdinalIgnoreCase)) return false;

        var fileName = path[(RequestPath.Length + 1)..^PreviewSuffix.Length];
        var separator = fileName.IndexOf('.');
        if (separator <= 0 || separator == fileName.Length - 1) return false;

        templateName = fileName[..separator];
        locale = fileName[(separator + 1)..];
        if (SupportedCultures.ToExactSupportedLocale(locale) is not { } supportedLocale) return false;

        locale = supportedLocale;
        return true;
    }

    extension(IApplicationBuilder app)
    {
        public IApplicationBuilder UseEmailPreviews()
        {
            return app.Use(async (context, next) =>
                {
                    if (!TryReadPreviewRequest(context.Request.Path.Value, out var templateName, out var locale))
                    {
                        await next();
                        return;
                    }

                    var brand = context.RequestServices.GetRequiredService<EmailBrand>();
                    var template = CreateSample(templateName, locale, brand);
                    if (template is null)
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }

                    var rendered = await context.RequestServices.GetRequiredService<IEmailRenderer>().RenderEmailAsync(template);
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.WriteAsync(rendered.HtmlBody);
                }
            );
        }

        public IApplicationBuilder UseEmailPreviewsNotFound()
        {
            return app.Use(async (context, next) =>
                {
                    if (!TryReadPreviewRequest(context.Request.Path.Value, out _, out _))
                    {
                        await next();
                        return;
                    }

                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                }
            );
        }
    }
}
