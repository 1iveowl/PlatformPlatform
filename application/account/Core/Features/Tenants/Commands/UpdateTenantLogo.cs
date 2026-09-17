using System.Security.Cryptography;
using Account.Features.Tenants.Domain;
using Account.Features.Users.Domain;
using FluentValidation;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Authentication;
using SharedKernel.Cqrs;
using SharedKernel.ExecutionContext;
using SharedKernel.Integrations.BlobStorage;
using SharedKernel.Telemetry;
using SharedKernel.Validation;

namespace Account.Features.Tenants.Commands;

[PublicAPI]
public sealed record UpdateTenantLogoCommand(Stream FileStream, string ContentType) : ICommand, IRequest<Result>
{
    public const int MaximumFileSizeInBytes = 2 * 1024 * 1024;
}

public sealed class UpdateTenantLogoValidator : AbstractValidator<UpdateTenantLogoCommand>
{
    public UpdateTenantLogoValidator()
    {
        RuleFor(x => x.ContentType)
            .Must(x => x is "image/jpeg" or "image/png" or "image/gif" or "image/webp")
            .WithMessage(_ => "Image must be of type JPEG, PNG, GIF, or WebP.");

        RuleFor(x => x.FileStream.Length)
            .LessThanOrEqualTo(UpdateTenantLogoCommand.MaximumFileSizeInBytes)
            .WithMessage(_ => "Image must be smaller than 2 MB");

        RuleFor(x => x.FileStream)
            .MustAsync(async (stream, cancellationToken) => await ImageContentInspector.InspectAsync(stream, UpdateTenantLogoCommand.MaximumFileSizeInBytes, cancellationToken) is not null)
            .WithMessage(_ => ImageContentInspector.InvalidImageMessage)
            .When(x => x.FileStream.Length <= UpdateTenantLogoCommand.MaximumFileSizeInBytes);
    }
}

public sealed class UpdateTenantLogoHandler(
    ITenantRepository tenantRepository,
    IExecutionContext executionContext,
    [FromKeyedServices("account-storage")] IBlobStorageClient blobStorageClient,
    ITelemetryEventsCollector events
)
    : IRequestHandler<UpdateTenantLogoCommand, Result>
{
    // Logos are public: the gateway's /logos route serves them without authentication to anyone holding the URL, and the
    // content hash in the blob name is not a secret. Tenant scoping governs who may replace a logo, not who may read it.
    private const string ContainerName = "logos";

    public async Task<Result> Handle(UpdateTenantLogoCommand command, CancellationToken cancellationToken)
    {
        if (executionContext.UserInfo.Role != nameof(UserRole.Owner))
        {
            return Result.Forbidden("Only owners are allowed to update tenant logo.");
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

        // The declared content type is untrusted, so the blob is stored and served with the type of the validated content
        var image = await ImageContentInspector.InspectAsync(command.FileStream, UpdateTenantLogoCommand.MaximumFileSizeInBytes, cancellationToken);
        if (image is null)
        {
            return Result.BadRequest(ImageContentInspector.InvalidImageMessage);
        }

        var fileHash = await GetFileHash(command.FileStream, cancellationToken);
        var blobName = $"{tenant.Id}/logo/{fileHash}.{image.FileExtension}";
        var logoUrl = $"/{ContainerName}/{blobName}";

        if (tenant.Logo.Url != logoUrl)
        {
            await blobStorageClient.UploadAsync(ContainerName, blobName, image.ContentType, command.FileStream, cancellationToken);

            tenant.UpdateLogo(logoUrl);
            tenantRepository.Update(tenant);

            events.CollectEvent(new TenantLogoUpdated(image.ContentType, command.FileStream.Length));
        }

        return Result.Success();
    }

    private static async Task<string> GetFileHash(Stream fileStream, CancellationToken cancellationToken)
    {
        using var sha1 = SHA1.Create();
        var hashBytes = await sha1.ComputeHashAsync(fileStream, cancellationToken);
        fileStream.Position = 0;
        // This just needs to be unique for one tenant, who likely will ever only have one logo, so 16 chars should be enough
        return BitConverter.ToString(hashBytes).Replace("-", "")[..16].ToUpper();
    }
}
