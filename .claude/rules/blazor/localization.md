---
paths: blazor/**/*.razor,blazor/**/*.cs,application/shared-kernel/SharedKernel.Localization/**
description: Rules for user-visible text in Blazor through the shared resources in en-US and da-DK, and for the culture across prerender and the browser
---

# Localization

Every user-visible string in the Blazor edition comes from `application/shared-kernel/SharedKernel.Localization`, a platform-neutral project whose `.resx` files generate a public strongly typed class each (`CommonStrings`, `AuthenticationStrings`, `UsersStrings`, `AccountStrings`, `FluentComponentStrings`). The host picks the culture per request and writes it to `<html lang>`; the WebAssembly client reads that attribute before its first render, so prerendered and interactive markup show the same language and formats.

## Implementation

1. Use the generated class for every string in a component or service: `@CommonStrings.LogIn`, `AuthenticationStrings.LoginExpired`. Never type a resource key as a string and never write user-visible text in a `.razor` or `.cs` file. Development-only fixture pages and API error text are the exceptions; API messages arrive already localized by `X-Locale` or stay English.
2. Add a key to the neutral file (`<Group>Strings.resx`, en-US) and to `<Group>Strings.da-DK.resx` in the same commit; `LocalizationResourceTests` fails on a key missing from either. Use sentence case, the wording the React catalogs use for the same text, and real Danish characters (æøå), never ASCII substitutes. Put a string in `CommonStrings` when more than one feature uses it, otherwise in the feature's group.
3. Format placeholders with `string.Format(CultureInfo.CurrentCulture, <Group>Strings.Key, value)`; resources use `{0}`-style placeholders. Format numbers and dates for display with `CultureInfo.CurrentCulture`, and use `CultureInfo.InvariantCulture` only for values sent to the API or written to a URL.
4. Put validation messages on the form model with `ErrorMessageResourceType` set to the group's class and `ErrorMessageResourceName = nameof(<Group>Strings.Key)` (`typeof(CommonStrings)` and `nameof(CommonStrings.Key)` for a shared message), so no framework default English message reaches a page.
5. Leave culture selection to the platform: `HostShell.GetLocale` is the only request culture provider, registered after `UseAuthentication`, and picks in this order: a signed-in user's locale claim, then the `preferred-locale` cookie when it names a supported culture exactly, then the best `Accept-Language` match, then en-US. The cookie is an untrusted hint the host validates; the public navigation's `LanguageMenu` and the preferences page write it through `wwwroot/js/theme.js` (`LocalePreference`), and a signed-in user's language is saved on the user first so the claim wins after login. Never read the query string, the framework's culture cookie or the browser language, and never set a culture per component; a language change reaches the host by loading the document again.
6. Keep the client on the host's culture: `App.razor` renders `<html lang="@CultureInfo.CurrentUICulture.Name">`, and `Blazor.Client/Program.cs` calls `ClientCulture.Apply` before `RunAsync`. The client project sets `BlazorWebAssemblyLoadAllGlobalizationData` because the culture may differ from the browser language; do not remove it.
7. Send the culture with every API call through the registered `LocaleHeaderHandler` (`X-Locale` from `CurrentUICulture`); the host adapter adds the same header from `HostShell.GetLocale`.
8. Localize the component library's built-in text through `FluentResourceLocalizer`, set as `LibraryConfiguration.Localizer` in both containers. Add a key to `FluentComponentStrings` when a component's built-in text becomes visible; an uncovered key falls back to the library's English default.

## Examples

### Example 1 - Text and Placeholders in a Component

```razor
@* ✅ DO: generated classes and a culture-aware format (blazor/Blazor.Host/Components/Pages/Public/LoginVerify.razor) *@
<PageTitle>@CommonStrings.EnterYourVerificationCode</PageTitle>

<h1>@CommonStrings.EnterYourVerificationCode</h1>

<p data-testid="verify-email">@string.Format(CultureInfo.CurrentCulture, AuthenticationStrings.CheckEmailForVerificationCode, Email)</p>

@* ❌ DON'T: literal text, a typed key, or a format without a culture *@
<h1>Enter your verification code</h1>
<p>@Localizer["CheckEmailForVerificationCode"]</p>
<p>@string.Format(AuthenticationStrings.CheckEmailForVerificationCode, Email)</p>
```

### Example 2 - Validation Messages From Resources

```csharp
// ✅ DO: every data annotation names a resource (blazor/Blazor.Host/Components/Pages/Public/PublicForms.cs)
public sealed class EmailForm
{
    [Required(ErrorMessageResourceType = typeof(CommonStrings), ErrorMessageResourceName = nameof(CommonStrings.EmailAddressRequired))]
    [EmailAddress(ErrorMessageResourceType = typeof(CommonStrings), ErrorMessageResourceName = nameof(CommonStrings.EmailAddressInvalid))]
    [StringLength(100, ErrorMessageResourceType = typeof(CommonStrings), ErrorMessageResourceName = nameof(CommonStrings.EmailAddressTooLong))]
    public string Email { get; set; } = "";
}

// ❌ DON'T: an English message the da-DK page would show unchanged
[Required(ErrorMessage = "Email address required")]
public string Email { get; set; } = "";
```

### Example 3 - One Culture From Prerender to the Browser

```csharp
// ✅ DO: the host's selection (claim, preferred-locale cookie, Accept-Language, en-US) is the only provider (blazor/Blazor.Host/HostApplication.cs)
options.RequestCultureProviders.Clear();
options.RequestCultureProviders.Add(new CustomRequestCultureProvider(context => Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(HostShell.GetLocale(context)))));

// ✅ DO: the client adopts the document's culture before the first render (blazor/Blazor.Client/Program.cs)
ClientCulture.Apply((IJSInProcessRuntime)host.Services.GetRequiredService<IJSRuntime>());

await host.RunAsync();

// ❌ DON'T: a culture from the browser or from a cookie in the client; it would differ from the prerendered markup and flash
CultureInfo.CurrentUICulture = new CultureInfo(await JS.InvokeAsync<string>("blazorCulture.get"));
```
