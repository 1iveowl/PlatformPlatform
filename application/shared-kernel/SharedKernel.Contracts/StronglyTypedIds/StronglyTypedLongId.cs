using System.Diagnostics.CodeAnalysis;

namespace SharedKernel.StronglyTypedIds;

/// <summary>
///     This is a special version of <see cref="StronglyTypedId{TValue,T}" /> for longs which is the recommended type to
///     use.
///     New IDs are chronological and guaranteed to be unique; generating them is server behavior provided by SharedKernel,
///     so parsing and serialization never depend on network interface discovery.
/// </summary>
public abstract record StronglyTypedLongId<T>(long Value)
    : StronglyTypedId<long, T>(Value), IParsableStronglyTypedId<T>
    where T : StronglyTypedLongId<T>
{
    public static bool TryParse(string? value, [NotNullWhen(true)] out T? result)
    {
        var success = long.TryParse(value, out var parsedValue);
        result = success ? FromLong(parsedValue) : null;
        return success;
    }

    internal static T FromLong(long value)
    {
        return (T)Activator.CreateInstance(
            typeof(T),
            BindingFlags.Instance | BindingFlags.Public,
            null,
            [value],
            null
        )!;
    }
}
