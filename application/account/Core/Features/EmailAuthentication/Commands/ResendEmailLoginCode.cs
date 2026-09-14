using Account.Features.EmailAuthentication.Domain;
using Account.Features.EmailAuthentication.EmailTemplates;
using Account.Features.EmailAuthentication.Shared;
using Account.Features.Users.Domain;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Identity;
using SharedKernel.Authentication;
using SharedKernel.Cqrs;
using SharedKernel.Emails;
using SharedKernel.Integrations.Email;
using SharedKernel.Telemetry;

namespace Account.Features.EmailAuthentication.Commands;

[PublicAPI]
public sealed record ResendEmailLoginCodeCommand : ICommand, IRequest<Result<ResendEmailLoginCodeResponse>>
{
    [JsonIgnore] // Removes this property from the API contract
    public EmailLoginId Id { get; init; } = null!;
}

public sealed class ResendEmailLoginCodeHandler(
    IEmailLoginRepository emailLoginRepository,
    IUserRepository userRepository,
    IEmailRenderer emailRenderer,
    IEmailClient emailClient,
    IPasswordHasher<object> passwordHasher,
    ITelemetryEventsCollector events,
    TimeProvider timeProvider,
    ILogger<ResendEmailLoginCodeHandler> logger
) : IRequestHandler<ResendEmailLoginCodeCommand, Result<ResendEmailLoginCodeResponse>>
{
    public async Task<Result<ResendEmailLoginCodeResponse>> Handle(ResendEmailLoginCodeCommand codeCommand, CancellationToken cancellationToken)
    {
        var emailLogin = await emailLoginRepository.GetByIdAsync(codeCommand.Id, cancellationToken);
        if (emailLogin is null) return Result<ResendEmailLoginCodeResponse>.NotFound($"Email login with id '{codeCommand.Id}' not found.");

        if (emailLogin.Completed)
        {
            logger.LogWarning("Email login with id '{EmailLoginId}' has already been completed", emailLogin.Id);
            return Result<ResendEmailLoginCodeResponse>.BadRequest($"The email login with id '{emailLogin.Id}' has already been completed.");
        }

        if (emailLogin.ResendCount >= EmailLogin.MaxResends)
        {
            events.CollectEvent(new EmailLoginCodeResendBlocked(emailLogin.Id, emailLogin.Type, emailLogin.RetryCount));
            return Result<ResendEmailLoginCodeResponse>.Forbidden("Too many attempts, please request a new code.", true);
        }

        var oneTimePassword = OneTimePasswordHelper.GenerateOneTimePassword(6);
        var oneTimePasswordHash = passwordHasher.HashPassword(this, oneTimePassword);
        emailLogin.UpdateVerificationCode(oneTimePasswordHash, timeProvider.GetUtcNow());
        emailLoginRepository.Update(emailLogin);

        var secondsSinceStarted = (timeProvider.GetUtcNow() - emailLogin.CreatedAt).TotalSeconds;
        events.CollectEvent(new EmailLoginCodeResend((int)secondsSinceStarted));

        var user = await userRepository.GetUserByEmailUnfilteredAsync(emailLogin.Email, cancellationToken);
        var locale = user is { Locale.Length: > 0 } ? user.Locale : "en-US";
        var template = new ResendEmailLoginEmailTemplate(
            locale,
            new ResendEmailLoginEmailModel(oneTimePassword, EmailDomainHelper.GetPublicHost(), EmailLogin.ValidForSeconds / 60)
        );
        var rendered = emailRenderer.RenderEmail(template);
        await emailClient.SendAsync(
            new EmailMessage(emailLogin.Email, rendered.Subject, rendered.HtmlBody, rendered.PlainTextBody),
            cancellationToken
        );

        return new ResendEmailLoginCodeResponse(EmailLogin.ValidForSeconds);
    }
}
