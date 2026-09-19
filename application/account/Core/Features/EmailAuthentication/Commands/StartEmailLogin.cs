using Account.Emails.Templates;
using Account.Features.EmailAuthentication.Domain;
using Account.Features.EmailAuthentication.Shared;
using Account.Features.Users.Domain;
using FluentValidation;
using JetBrains.Annotations;
using SharedKernel.Cqrs;
using SharedKernel.Emails;
using SharedKernel.ExecutionContext;
using SharedKernel.Integrations.Email;
using SharedKernel.SinglePageApp;
using SharedKernel.Telemetry;
using SharedKernel.Validation;

namespace Account.Features.EmailAuthentication.Commands;

[PublicAPI]
public sealed record StartEmailLoginCommand(string Email) : ICommand, IRequest<Result<StartEmailLoginResponse>>
{
    public string Email { get; init; } = Email.Trim().ToLower();
}

public sealed class StartEmailLoginValidator : AbstractValidator<StartEmailLoginCommand>
{
    public StartEmailLoginValidator()
    {
        RuleFor(x => x.Email).SetValidator(new SharedValidations.Email());
    }
}

public sealed class StartEmailLoginHandler(
    IUserRepository userRepository,
    IEmailRenderer emailRenderer,
    IEmailClient emailClient,
    StartEmailConfirmation startEmailConfirmation,
    IExecutionContext executionContext,
    ITelemetryEventsCollector events
) : IRequestHandler<StartEmailLoginCommand, Result<StartEmailLoginResponse>>
{
    public async Task<Result<StartEmailLoginResponse>> Handle(StartEmailLoginCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetUserByEmailUnfilteredAsync(command.Email, cancellationToken);

        if (user is null)
        {
            var anonymousLocale = executionContext.UserInfo.Locale ?? "en-US";
            var publicUrl = Environment.GetEnvironmentVariable(SinglePageAppConfiguration.PublicUrlKey) ?? string.Empty;
            var signupUrl = string.IsNullOrEmpty(publicUrl) ? "/signup" : $"{publicUrl}/signup";
            var unknownTemplate = new UnknownUserEmailTemplate(
                anonymousLocale,
                new UnknownUserEmailModel(command.Email, signupUrl)
            );
            var unknownRendered = await emailRenderer.RenderEmailAsync(unknownTemplate);
            await emailClient.SendAsync(
                new EmailMessage(command.Email.ToLower(), unknownRendered.Subject, unknownRendered.HtmlBody, unknownRendered.PlainTextBody),
                cancellationToken
            );

            return new StartEmailLoginResponse(EmailLoginId.NewId(), EmailLogin.ValidForSeconds);
        }

        var locale = string.IsNullOrEmpty(user.Locale) ? "en-US" : user.Locale;
        var domain = EmailDomainHelper.GetPublicHost();
        var expiryMinutes = EmailLogin.ValidForSeconds / 60;

        var result = await startEmailConfirmation.StartAsync(
            user.Email,
            EmailLoginType.Login,
            oneTimePassword => new StartLoginEmailTemplate(locale, new StartLoginEmailModel(oneTimePassword, domain, expiryMinutes)),
            cancellationToken
        );

        if (!result.IsSuccess) return Result<StartEmailLoginResponse>.From(result);

        events.CollectEvent(new EmailLoginStarted(user.Id));

        return new StartEmailLoginResponse(result.Value!, EmailLogin.ValidForSeconds);
    }
}
