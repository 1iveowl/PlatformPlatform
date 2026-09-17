using Account.Features.Users.Commands;
using Account.Features.Users.Queries;
using Microsoft.AspNetCore.Mvc;
using SharedKernel.ApiResults;
using SharedKernel.Domain;
using SharedKernel.Endpoints;
using SharedKernel.OpenApi;
using UserRequests = Account.Features.Users.Requests;

namespace Account.Api.Endpoints;

public sealed class UserEndpoints : IEndpoints
{
    private const string RoutesPrefix = "/api/account/users";

    // Room for the multipart boundaries and part headers around the file, so the request limit never cuts a valid upload
    private const int MultipartOverheadInBytes = 64 * 1024;

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup(RoutesPrefix).WithTags("Users").WithGroupName(OpenApiDocumentNames.Account).RequireAuthorization().ProducesValidationProblem();

        group.MapGet("/", async Task<ApiResult<UsersResponse>> ([AsParameters] UserRequests.GetUsersQuery query, IMediator mediator)
            => await mediator.Send(new GetUsersQuery(query.Search, query.UserRole, query.UserStatus, query.StartDate, query.EndDate, query.OrderBy, query.SortOrder, query.PageOffset, query.PageSize))
        ).Produces<UsersResponse>();

        group.MapGet("/{id}", async Task<ApiResult<UserDetails>> (UserId id, IMediator mediator)
            => await mediator.Send(new GetUserByIdQuery(id))
        ).Produces<UserDetails>();

        group.MapGet("/summary", async Task<ApiResult<UserSummaryResponse>> (IMediator mediator)
            => await mediator.Send(new GetUserSummaryQuery())
        ).Produces<UserSummaryResponse>();

        group.MapDelete("/{id}", async Task<ApiResult> (UserId id, IMediator mediator)
            => await mediator.Send(new DeleteUserCommand(id))
        );

        group.MapPost("/bulk-delete", async Task<ApiResult> (UserRequests.BulkDeleteUsersCommand command, IMediator mediator)
            => await mediator.Send(new BulkDeleteUsersCommand(command.UserIds))
        );

        group.MapPut("/{id}/change-user-role", async Task<ApiResult> (UserId id, UserRequests.ChangeUserRoleCommand command, IMediator mediator)
            => await mediator.Send(new ChangeUserRoleCommand { Id = id, UserRole = command.UserRole })
        );

        group.MapPost("/invite", async Task<ApiResult> (InviteUserCommand command, IMediator mediator)
            => await mediator.Send(command)
        );

        group.MapPost("/decline-invitation", async Task<ApiResult> (DeclineInvitationCommand command, IMediator mediator)
            => await mediator.Send(command)
        );

        group.MapGet("/deleted", async Task<ApiResult<DeletedUsersResponse>> ([AsParameters] GetDeletedUsersQuery query, IMediator mediator)
            => await mediator.Send(query)
        ).Produces<DeletedUsersResponse>();

        group.MapPost("/{id}/restore", async Task<ApiResult> (UserId id, IMediator mediator)
            => await mediator.Send(new RestoreUserCommand(id))
        );

        group.MapDelete("/{id}/purge", async Task<ApiResult> (UserId id, IMediator mediator)
            => await mediator.Send(new PurgeUserCommand(id))
        );

        group.MapPost("/deleted/bulk-purge", async Task<ApiResult> (BulkPurgeUsersCommand command, IMediator mediator)
            => await mediator.Send(command)
        );

        group.MapPost("/deleted/empty-recycle-bin", async Task<ApiResult<int>> (IMediator mediator)
            => await mediator.Send(new EmptyRecycleBinCommand())
        ).Produces<int>();

        // The following endpoints are for the current user only
        group.MapGet("/me", async Task<ApiResult<CurrentUserResponse>> ([AsParameters] GetUserQuery query, IMediator mediator)
            => await mediator.Send(query)
        ).Produces<CurrentUserResponse>();

        group.MapPut("/me", async Task<ApiResult> (UserRequests.UpdateCurrentUserCommand command, IMediator mediator)
            => (await mediator.Send(new UpdateCurrentUserCommand(command.FirstName, command.LastName, command.Title))).AddRefreshAuthenticationTokens()
        );

        group.MapPost("/me/update-avatar", async Task<ApiResult> (IFormFile file, IMediator mediator)
            => await mediator.Send(new UpdateAvatarCommand(file.OpenReadStream(), file.ContentType))
        ).WithFormOptions(multipartBodyLengthLimit: UpdateAvatarCommand.MaximumFileSizeInBytes).WithMetadata(new RequestSizeLimitAttribute(UpdateAvatarCommand.MaximumFileSizeInBytes + MultipartOverheadInBytes));

        group.MapDelete("/me/remove-avatar", async Task<ApiResult> (IMediator mediator)
            => await mediator.Send(new RemoveAvatarCommand())
        );

        group.MapPut("/me/change-locale", async Task<ApiResult> (ChangeLocaleCommand command, IMediator mediator)
            => (await mediator.Send(command)).AddRefreshAuthenticationTokens()
        );

        group.MapPut("/me/change-zoom-level", async Task<ApiResult> (ChangeZoomLevelCommand command, IMediator mediator)
            => await mediator.Send(command)
        );

        group.MapPut("/me/change-theme", async Task<ApiResult> (ChangeThemeCommand command, IMediator mediator)
            => await mediator.Send(command)
        );
    }
}
