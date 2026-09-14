using JetBrains.Annotations;

namespace Account.Features.Users.Domain;

[PublicAPI]
public enum UserPurgeReason
{
    SingleUserPurge,
    BulkUserPurge,
    RecycleBinPurge
}
