using System.Text.Json;

namespace SharedKernel.ApiResults;

// The JSON conventions of every API: enums as strings and camelCase property names. The server configures its
// serializer and OpenAPI generation from these options, and a client must use the same options so enums without a
// [JsonConverter] attribute (for example SortOrder) and IDs round-trip exactly as the server writes them.
public static class ApiJsonSerializerOptions
{
    public static JsonSerializerOptions Create()
    {
        return new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}
