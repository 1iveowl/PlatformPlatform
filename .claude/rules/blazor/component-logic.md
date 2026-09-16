---
paths: blazor/**/*.razor
description: Rules keeping business logic out of Blazor components; components bind, call the typed client or a service, and render
---

# Component Logic

A Razor component binds inputs, calls the typed client or a service, and renders the result. Validation beyond data annotations, permission decisions and state transitions live on the server or in a class with its own tests. This keeps a rule in one place, testable without a renderer, and shared between the static and interactive surfaces.

## Implementation

1. Keep the `@code` block to state fields, parameters, lifecycle methods, event handlers that call a client or a service, and small render helpers. A method that parses, normalizes, decides or computes belongs in a class next to the component (`UsersListSource`, `AppUrls`, `ApiFailureClassifier`) with a unit test in `blazor/Blazor.Tests`.
2. Leave validation beyond data annotations to the server: the command's validator rejects the request and the field errors arrive through `FormErrorMapper`. A component never repeats a server rule, checks a value the server checks, or builds its own error messages for them.
3. Leave permission decisions to the server: an endpoint returns a failure result the presentation shows. A component may hide or disable a control from a fact the bootstrap or an API response states (the user's `Role`, an item's owner), but the derivation of what a role may do lives in one class with a test, never repeated across components.
4. Drive state transitions through the component built for them: `UnsavedChangesGuard` for dirty and clean, `DirtyDialog` for a guarded dialog, `DataListController` through `DataList` for list state, `AuthenticationNavigator` for leaving the authenticated surface. A feature component sets the inputs of those components and reacts to their callbacks; it does not reimplement the transitions.
5. Put parsing and normalization of URL and form values in the feature's source class, with names only for enums, exact formats for dates, and malformed values dropped; the component passes the values through.
6. Keep presentation-only decisions in the component: which text to show, whether a section renders, the test id, the class name. That is what a component is for.

## Examples

### Example 1 - Normalization Before and After

```razor
@* ❌ DON'T: before, the parsing and canonical form inside the component *@
@code {

    private IReadOnlyDictionary<string, string> Normalize(IReadOnlyDictionary<string, string> filters)
    {
        var normalized = new Dictionary<string, string>();
        if (filters.TryGetValue("userRole", out var role) && Enum.TryParse<UserRole>(role, true, out var parsedRole)) normalized["userRole"] = parsedRole.ToString();
        if (filters.TryGetValue("startDate", out var start) && DateTime.TryParse(start, out var parsedStart)) normalized["startDate"] = parsedStart.ToString("yyyy-MM-dd");
        return normalized;
    }

}
```

```csharp
// ✅ DO: after, the same rules in a tested static class the component passes to the list
// (blazor/Blazor.Client/Users/UsersListSource.cs, tested in blazor/Blazor.Tests/Client/Lists/UsersListSourceTests.cs)
public static IReadOnlyDictionary<string, string> NormalizeFilters(IReadOnlyDictionary<string, string> filters)
{
    var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
    if (filters.TryGetValue(SearchParameter, out var search) && !string.IsNullOrWhiteSpace(search)) normalized[SearchParameter] = search.Trim();
    if (ParseEnum<UserRole>(filters.GetValueOrDefault(UserRoleParameter)) is { } role) normalized[UserRoleParameter] = role.ToString();
    if (ParseDate(filters.GetValueOrDefault(StartDateParameter)) is { } startDate) normalized[StartDateParameter] = startDate.ToString(DateFormat, CultureInfo.InvariantCulture);
    return normalized;
}
```

```razor
@* ✅ DO: the component only wires the source in (blazor/Blazor.Client/Users/UsersSurface.razor) *@
<DataList ... FilterParameters="UsersListSource.FilterParameters" NormalizeFilters="UsersListSource.NormalizeFilters" .../>
```

### Example 2 - A Decision Made Once

```csharp
// ✅ DO: the return path rule lives in one class the host and the client share (blazor/Blazor.Client/AppUrls.cs)
public static string SanitizeReturnPath(string? returnPath)
{
    if (string.IsNullOrEmpty(returnPath)) return AuthenticatedHome;
    if (!returnPath.StartsWith($"{PathBase}/", StringComparison.Ordinal) || returnPath.Contains("//") || returnPath.Contains('\\')) return AuthenticatedHome;
    return returnPath;
}

// ❌ DON'T: the rule again in a component, with a different edge case
Navigation.NavigateTo(ReturnPath?.StartsWith("/blazor") == true ? ReturnPath : "/blazor/app", true);

// ❌ DON'T: a server rule re-checked in a component; the validator already rejects it and the mapper shows the message
if (Input.Email.Length > 100) _formErrors.AddFormMessage("The email is too long.");
```
