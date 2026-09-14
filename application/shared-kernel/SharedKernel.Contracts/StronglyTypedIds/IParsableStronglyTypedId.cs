using System.Diagnostics.CodeAnalysis;

namespace SharedKernel.StronglyTypedIds;

/// <summary>
///     Parsing of a strongly typed ID, called statically by
///     <see cref="StronglyTypedIdJsonConverter{TValue,TStronglyTypedId}" />
///     so the parse method survives trimming in a client without being located by reflection.
/// </summary>
public interface IParsableStronglyTypedId<TSelf> where TSelf : IParsableStronglyTypedId<TSelf>
{
    static abstract bool TryParse(string? value, [NotNullWhen(true)] out TSelf? result);
}
