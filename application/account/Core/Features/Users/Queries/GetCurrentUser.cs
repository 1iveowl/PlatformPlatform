using Account.Features.Users.Domain;
using JetBrains.Annotations;
using Mapster;
using SharedKernel.Cqrs;

namespace Account.Features.Users.Queries;

[PublicAPI]
public sealed record GetUserQuery : IRequest<Result<CurrentUserResponse>>;

public sealed class GetUserHandler(IUserRepository userRepository)
    : IRequestHandler<GetUserQuery, Result<CurrentUserResponse>>
{
    public async Task<Result<CurrentUserResponse>> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetLoggedInUserAsync(cancellationToken);
        return user.Adapt<CurrentUserResponse>();
    }
}
