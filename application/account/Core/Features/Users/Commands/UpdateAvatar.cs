using Account.Features.Users.Domain;
using Account.Features.Users.Shared;
using FluentValidation;
using JetBrains.Annotations;
using SharedKernel.Cqrs;
using SharedKernel.Telemetry;
using SharedKernel.Validation;

namespace Account.Features.Users.Commands;

[PublicAPI]
public sealed record UpdateAvatarCommand(Stream FileSteam, string ContentType) : ICommand, IRequest<Result>
{
    public const int MaximumFileSizeInBytes = 1024 * 1024;
}

public sealed class UpdateAvatarValidator : AbstractValidator<UpdateAvatarCommand>
{
    public UpdateAvatarValidator()
    {
        RuleFor(x => x.ContentType)
            .Must(x => x is "image/jpeg" or "image/png" or "image/gif" or "image/webp") // Align with frontend
            .WithMessage(_ => "Image must be of type JPEG, PNG, GIF, or WebP.");

        RuleFor(x => x.FileSteam.Length)
            .LessThanOrEqualTo(UpdateAvatarCommand.MaximumFileSizeInBytes)
            .WithMessage(_ => "Image must be smaller than 1 MB");

        RuleFor(x => x.FileSteam)
            .MustAsync(async (stream, cancellationToken) => await ImageContentInspector.InspectAsync(stream, UpdateAvatarCommand.MaximumFileSizeInBytes, cancellationToken) is not null)
            .WithMessage(_ => ImageContentInspector.InvalidImageMessage)
            .When(x => x.FileSteam.Length <= UpdateAvatarCommand.MaximumFileSizeInBytes);
    }
}

public sealed class UpdateAvatarHandler(IUserRepository userRepository, AvatarUpdater avatarUpdater, ITelemetryEventsCollector events)
    : IRequestHandler<UpdateAvatarCommand, Result>
{
    public async Task<Result> Handle(UpdateAvatarCommand command, CancellationToken cancellationToken)
    {
        var image = await ImageContentInspector.InspectAsync(command.FileSteam, UpdateAvatarCommand.MaximumFileSizeInBytes, cancellationToken);
        if (image is null)
        {
            return Result.BadRequest(ImageContentInspector.InvalidImageMessage);
        }

        var user = await userRepository.GetLoggedInUserAsync(cancellationToken);

        // The declared content type is untrusted, so the blob is stored and served with the type of the validated content
        if (await avatarUpdater.UpdateAvatar(user, false, image.ContentType, command.FileSteam, cancellationToken))
        {
            events.CollectEvent(new UserAvatarUpdated(image.ContentType, command.FileSteam.Length));
        }

        return Result.Success();
    }
}
