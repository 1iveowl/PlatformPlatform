// The value forms list sources share in their URL filters and sort keys. A source keeps its own parameter names, defaults,
// allowed values and URL composition; these only read and write the values. Enum names are matched as names, never as numbers
// (Enum.TryParse would accept "1"), and never trimmed; the caller states whether case matters, because the sources differ.

namespace Blazor.Client.Components.Lists;

public static class DataListQueryValues
{
    public static TEnum? ParseName<TEnum>(string? value, StringComparison comparison) where TEnum : struct, Enum
    {
        return Enum.GetValues<TEnum>().Cast<TEnum?>().FirstOrDefault(candidate => string.Equals(candidate.ToString(), value, comparison));
    }

    // A JSON array of enum names as the React router writes it (["Premium","Standard"]), or a single bare name. The value and
    // each entry are trimmed, names match ignoring case, unknown and unquoted entries and values outside the canonical order are
    // dropped, and the values come back distinct in the canonical order
    public static IReadOnlyList<TEnum> ParseValues<TEnum>(string? value, IReadOnlyList<TEnum> canonicalOrder) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        var trimmed = value.Trim();
        var names = trimmed.StartsWith('[') && trimmed.EndsWith(']')
            ? trimmed[1..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(Unquote)
            : [trimmed];
        var parsed = names.Select(name => name is null ? null : ParseName<TEnum>(name, StringComparison.OrdinalIgnoreCase)).OfType<TEnum>().ToHashSet();
        return canonicalOrder.Where(parsed.Contains).ToArray();
    }

    // The URL form of a multi-value filter, or null when no value is selected so the parameter is left out
    public static string? FormatValues<TEnum>(IReadOnlyList<TEnum> values) where TEnum : struct, Enum
    {
        return values.Count == 0 ? null : $"[{string.Join(',', values.Select(value => $"\"{value}\""))}]";
    }

    // The value added when absent and removed when present, the result in the canonical order
    public static IReadOnlyList<TEnum> Toggle<TEnum>(IReadOnlyList<TEnum> values, TEnum value, IReadOnlyList<TEnum> canonicalOrder) where TEnum : struct, Enum
    {
        var selected = values.ToHashSet();
        if (!selected.Remove(value)) selected.Add(value);
        return canonicalOrder.Where(selected.Contains).ToArray();
    }

    private static string? Unquote(string token)
    {
        return token is ['"', .., '"'] ? token[1..^1] : null;
    }
}
