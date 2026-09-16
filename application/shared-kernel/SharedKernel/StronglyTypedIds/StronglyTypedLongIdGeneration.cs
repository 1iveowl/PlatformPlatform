namespace SharedKernel.StronglyTypedIds;

// ID generation is server behavior: IdGenerator discovers network interfaces, which a client must never depend on.
// The static extension member keeps the call site as TenantId.NewId().
public static class StronglyTypedLongIdGeneration
{
    public static T NewLongId<T>() where T : StronglyTypedLongId<T>
    {
        return StronglyTypedLongId<T>.FromLong(IdGenerator.NewId());
    }

    extension<T>(StronglyTypedLongId<T>) where T : StronglyTypedLongId<T>
    {
        public static T NewId()
        {
            return NewLongId<T>();
        }
    }
}
