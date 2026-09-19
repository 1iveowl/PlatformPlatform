using Account.Emails.Templates;
using Account.Features.Tenants.Domain;
using Account.Features.Users.Domain;
using FluentValidation;
using JetBrains.Annotations;
using SharedKernel.Authentication;
using SharedKernel.Cqrs;
using SharedKernel.Emails;
using SharedKernel.ExecutionContext;
using SharedKernel.Integrations.Email;
using SharedKernel.SinglePageApp;
using SharedKernel.Telemetry;
using SharedKernel.Validation;

namespace Account.Features.Users.Commands;

[PublicAPI]
public sealed record InviteUserCommand(string Email) : ICommand, IRequest<Result>
{
    public string Email { get; init; } = Email.Trim().ToLower();
}

public sealed class InviteUserValidator : AbstractValidator<InviteUserCommand>
{
    public InviteUserValidator()
    {
        RuleFor(x => x.Email).SetValidator(new SharedValidations.Email());
    }
}

public sealed class InviteUserHandler(
    IUserRepository userRepository,
    ITenantRepository tenantRepository,
    IEmailRenderer emailRenderer,
    IEmailClient emailClient,
    IExecutionContext executionContext,
    IMediator mediator,
    ITelemetryEventsCollector events
) : IRequestHandler<InviteUserCommand, Result>
{
    public async Task<Result> Handle(InviteUserCommand command, CancellationToken cancellationToken)
    {
        if (executionContext.UserInfo.Role != nameof(UserRole.Owner))
        {
            return Result.Forbidden("Only owners are allowed to invite other users.");
        }

        var tenant = await tenantRepository.GetCurrentTenantAsync(cancellationToken);
        if (tenant is null)
        {
            return Result.Unauthorized("Tenant has been deleted.", responseHeaders: new Dictionary<string, string>
                {
                    { AuthenticationTokenHttpKeys.UnauthorizedReasonHeaderKey, nameof(UnauthorizedReason.TenantDeleted) }
                }
            );
        }

        if (string.IsNullOrWhiteSpace(tenant.Name))
        {
            return Result.BadRequest("Account name must be set before inviting users.");
        }

        if (!await userRepository.IsEmailFreeAsync(command.Email, cancellationToken))
        {
            var deletedUser = await userRepository.GetDeletedUserByEmailAsync(command.Email, cancellationToken);
            if (deletedUser is not null)
            {
                return Result.BadRequest($"The user '{command.Email}' was previously deleted. Please restore or permanently delete the user before inviting again.");
            }

            return Result.BadRequest($"The user '{command.Email}' already exists.");
        }

        var result = await mediator.Send(
            new CreateUserCommand(executionContext.TenantId!, command.Email, UserRole.Member, false, null), cancellationToken
        );

        events.CollectEvent(new UserInvited(result.Value!));

        var loginPath = $"{Environment.GetEnvironmentVariable(SinglePageAppConfiguration.PublicUrlKey)}/login";
        var inviter = $"{executionContext.UserInfo.FirstName} {executionContext.UserInfo.LastName}".Trim();
        inviter = inviter.Length > 0 ? inviter : executionContext.UserInfo.Email ?? string.Empty;
        var inviterLocale = executionContext.UserInfo.Locale ?? "en-US";

        var template = new InviteUserEmailTemplate(
            inviterLocale,
            new InviteUserEmailModel(inviter, tenant.Name, command.Email.ToLower(), loginPath)
        );
        var rendered = await emailRenderer.RenderEmailAsync(template);
        await emailClient.SendAsync(
            new EmailMessage(command.Email.ToLower(), rendered.Subject, rendered.HtmlBody, rendered.PlainTextBody),
            cancellationToken
        );

        return Result.Success();
    }
}
