using NUlid;

namespace SharedKernel.StronglyTypedIds;

// ID generation is server behavior, kept out of the portable contracts that only parse and serialize IDs.
// The static extension member keeps the call site as UserId.NewId().
public static class StronglyTypedUlidGeneration
{
    public static T NewUlidId<T>() where T : StronglyTypedUlid<T>
    {
        return StronglyTypedUlid<T>.FromUlid(Ulid.NewUlid());
    }

    extension<T>(StronglyTypedUlid<T>) where T : StronglyTypedUlid<T>
    {
        public static T NewId()
        {
            return NewUlidId<T>();
        }
    }
}
