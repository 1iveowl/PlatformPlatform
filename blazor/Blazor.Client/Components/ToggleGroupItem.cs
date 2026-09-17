namespace Blazor.Client.Components;

// One choice of a ToggleGroup: the value it reports, its visible name and its test id
public sealed record ToggleGroupItem(string Value, string Label, string TestId);
