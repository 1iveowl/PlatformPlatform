using System.Text.Json;

namespace SharedKernel.StronglyTypedIds;

// TryParse is called statically through IParsableStronglyTypedId rather than located by reflection, so a trimmed client
// deserializes IDs exactly as the server does: the trimmer cannot see a method found by name and removes it.
public class StronglyTypedIdJsonConverter<TValue, TStronglyTypedId> : JsonConverter<TStronglyTypedId>
    where TStronglyTypedId : StronglyTypedId<TValue, TStronglyTypedId>, IParsableStronglyTypedId<TStronglyTypedId>
    where TValue : IComparable<TValue>
{
    public override TStronglyTypedId? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var stringValue = reader.GetString();
        if (stringValue is null)
        {
            return null;
        }

        if (TStronglyTypedId.TryParse(stringValue, out var result))
        {
            return result;
        }

        throw new JsonException($"Unable to convert {typeof(TStronglyTypedId).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, TStronglyTypedId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
