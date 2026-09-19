using Account.Features.EmailAuthentication.Domain;
using Microsoft.AspNetCore.Identity;
using SharedKernel.Authentication;
using SharedKernel.Cqrs;
using SharedKernel.Emails;
using SharedKernel.Integrations.Email;

namespace Account.Features.EmailAuthentication.Shared;

public sealed class StartEmailConfirmation(
    IEmailLoginRepository emailLoginRepository,
    IEmailRenderer emailRenderer,
    IEmailClient emailClient,
    IPasswordHasher<object> passwordHasher,
    TimeProvider timeProvider
)
{
    public async Task<Result<EmailLoginId>> StartAsync(
        string email,
        EmailLoginType type,
        Func<string, EmailTemplateBase> templateFactory,
        CancellationToken cancellationToken
    )
    {
        var existingLogins = emailLoginRepository.GetByEmail(email).ToArray();

        var lockoutMinutes = type == EmailLoginType.Signup ? -60 : -15;
        if (existingLogins.Count(r => r.CreatedAt > timeProvider.GetUtcNow().AddMinutes(lockoutMinutes)) >= 3)
        {
            return Result<EmailLoginId>.TooManyRequests("Too many attempts to confirm this email address. Please try again later.");
        }

        var oneTimePassword = OneTimePasswordHelper.GenerateOneTimePassword(6);
        var oneTimePasswordHash = passwordHasher.HashPassword(this, oneTimePassword);
        var emailLogin = EmailLogin.Create(email, oneTimePasswordHash, type);

        await emailLoginRepository.AddAsync(emailLogin, cancellationToken);

        var template = templateFactory(oneTimePassword);
        var rendered = await emailRenderer.RenderEmailAsync(template);
        await emailClient.SendAsync(
            new EmailMessage(emailLogin.Email, rendered.Subject, rendered.HtmlBody, rendered.PlainTextBody),
            cancellationToken
        );

        return emailLogin.Id;
    }
}
