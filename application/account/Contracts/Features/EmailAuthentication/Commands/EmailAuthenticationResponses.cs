using Account.Features.EmailAuthentication.Domain;
using JetBrains.Annotations;

namespace Account.Features.EmailAuthentication.Commands;

[PublicAPI]
public sealed record StartEmailLoginResponse(EmailLoginId EmailLoginId, int ValidForSeconds);

[PublicAPI]
public sealed record ResendEmailLoginCodeResponse(int ValidForSeconds);

[PublicAPI]
public sealed record StartEmailSignupResponse(EmailLoginId EmailLoginId, int ValidForSeconds);
