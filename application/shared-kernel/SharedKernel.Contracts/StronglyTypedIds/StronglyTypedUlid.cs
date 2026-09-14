using System.Diagnostics.CodeAnalysis;
using NUlid;

namespace SharedKernel.StronglyTypedIds;

/// <summary>
///     This is the recommended ID type to use. It uses the <see cref="Ulid" /> to create unique chronological IDs.
///     IDs are prefixed with the value of the <see cref="IdPrefixAttribute" /> inspired by Stripe's API.
///     Parsing is portable; generating a new ID is server behavior provided by SharedKernel.
/// </summary>
public abstract record StronglyTypedUlid<T>(string Value)
    : StronglyTypedId<string, T>(Value), IParsableStronglyTypedId<T>
    where T : StronglyTypedUlid<T>
{
    private static readonly string Prefix = typeof(T).GetCustomAttribute<IdPrefixAttribute>()?.Prefix
                                            ?? throw new InvalidOperationException("IdPrefixAttribute is required.");

    public static bool TryParse(string? value, [NotNullWhen(true)] out T? result)
    {
        if (value is null || !value.StartsWith($"{Prefix}_"))
        {
            result = null;
            return false;
        }

        if (!Ulid.TryParse(value.Replace($"{Prefix}_", ""), out var parsedValue))
        {
            result = null;
            return false;
        }

        result = FromUlid(parsedValue);
        return true;
    }

    internal static T FromUlid(Ulid value)
    {
        return (T)Activator.CreateInstance(
            typeof(T),
            BindingFlags.Instance | BindingFlags.Public,
            null,
            [$"{Prefix}_{value}"],
            null
        )!;
    }
}
