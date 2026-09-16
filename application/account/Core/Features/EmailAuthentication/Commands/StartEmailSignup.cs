using Account.Features.EmailAuthentication.Domain;
using Account.Features.EmailAuthentication.EmailTemplates;
using Account.Features.EmailAuthentication.Shared;
using FluentValidation;
using JetBrains.Annotations;
using SharedKernel.Cqrs;
using SharedKernel.ExecutionContext;
using SharedKernel.Telemetry;
using SharedKernel.Validation;

namespace Account.Features.EmailAuthentication.Commands;

[PublicAPI]
public sealed record StartEmailSignupCommand(string Email) : ICommand, IRequest<Result<StartEmailSignupResponse>>
{
    public string Email { get; } = Email.Trim().ToLower();
}

public sealed class StartEmailSignupValidator : AbstractValidator<StartEmailSignupCommand>
{
    public StartEmailSignupValidator()
    {
        RuleFor(x => x.Email).SetValidator(new SharedValidations.Email());
    }
}

public sealed class StartEmailSignupHandler(
    StartEmailConfirmation startEmailConfirmation,
    IExecutionContext executionContext,
    ITelemetryEventsCollector events
) : IRequestHandler<StartEmailSignupCommand, Result<StartEmailSignupResponse>>
{
    public async Task<Result<StartEmailSignupResponse>> Handle(StartEmailSignupCommand command, CancellationToken cancellationToken)
    {
        var locale = executionContext.UserInfo.Locale ?? "en-US";
        var domain = EmailDomainHelper.GetPublicHost();
        var expiryMinutes = EmailLogin.ValidForSeconds / 60;

        var result = await startEmailConfirmation.StartAsync(
            command.Email,
            EmailLoginType.Signup,
            oneTimePassword => new StartSignupEmailTemplate(locale, new StartSignupEmailModel(oneTimePassword, domain, expiryMinutes)),
            cancellationToken
        );

        if (!result.IsSuccess) return Result<StartEmailSignupResponse>.From(result);

        events.CollectEvent(new SignupStarted());

        return Result<StartEmailSignupResponse>.Success(new StartEmailSignupResponse(result.Value!, EmailLogin.ValidForSeconds));
    }
}
