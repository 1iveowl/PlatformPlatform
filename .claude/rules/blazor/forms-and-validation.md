---
paths: blazor/**/*.razor,blazor/**/Forms/*.cs,blazor/**/Pages/Public/*.cs
description: Rules for Blazor forms on static server-rendered pages and interactive surfaces, server error mapping, error presentation and the unsaved-changes guard
---

# Forms and Validation

Guidelines for building forms in the Blazor edition. Static server-rendered forms and interactive forms share one `EditForm` shape, one server error mapper and one failure classification; they differ in how a failure is shown (a form-level alert on a static page or inside a modal dialog, a toast elsewhere on an interactive surface) and in whether a submission crosses the authentication boundary.

## Implementation

1. Build a form on `EditForm` with an `EditContext` created in `OnInitialized`, a `DataAnnotationsValidator`, `InputText` and the other input components, and `ValidationMessage For="() => Input.Field"` under each input. Put data annotations on a sealed form model class with messages from the shared resources (`ErrorMessageResourceType`, `ErrorMessageResourceName`); public form models live in `Blazor.Host/Components/Pages/Public/PublicForms.cs`.
2. Build a static form as a POST to the host:
   - Give it a unique `FormName` and bind the model with `[SupplyParameterFromForm(FormName = "...")]`; the property getter supplies an empty model with `field ??= new ...()` when the request is not this form's post
   - Add `Enhance` when the result stays inside the public surface (a login start redirects to the verification page); leave it off when a successful submission crosses the authentication boundary (the verification forms), so the browser follows the redirect with a full document load and the session cookies the gateway set on it
   - Call the typed client from `OnValidSubmit` with `HttpContext.RequestAborted`, and redirect with `Navigation.NavigateTo(url)` for an enhanced form or `NavigateTo(url, true)` when the boundary is crossed
   - Never show a toast and never load WebAssembly on a static form; the response itself carries the messages
   - Post a body-less action (a resend) as a plain `<form method="post" @formname="..." @onsubmit="...">` with `<AntiforgeryToken/>` and a submit button, since there is no model to bind or validate (`LoginVerify.razor`, `SignupVerify.razor`); its failures go to the page's `FormErrorMapper` and `FormErrorAlert` like the main form's
3. Map a failed API call with `FormErrorMapper`, created on the `EditContext` and disposed with the component:
   - `ApplyFailure(result)` on a static form: field errors go to their fields (keys match the model's properties case-insensitively, unmatched keys become form messages), a message becomes a form message, a rejected antiforgery token and a client the write gate refused because it no longer matches its server both become a form message with `IsReloadRequired`, and a 401 or a cancellation shows nothing
   - `Apply(problem)` when only field errors should be placed, for instance from `ApiFailurePresenter`
   - Server messages live in the mapper's own `ValidationMessageStore`; the next submit clears them and leaves the data annotations messages alone. Never re-validate the server's rules in `@code`
   - Add `AddFormMessage(...)` for a failure the page detects before calling the API, such as a verification page opened without a login id
4. Render `<FormErrorAlert Errors="_formErrors"/>` after every static form. It is always rendered so an enhanced update fills an existing live region, it shows the form messages as text, and it adds a reload link with `data-enhance-nav="false"` when a new antiforgery token is needed.
5. Present failures on an interactive surface through `ApiFailurePresenter.Present(result, formErrors)`: field errors go to the form's mapper when one is given, a message becomes an error toast (detail, then title), a rejected antiforgery token and a refusal by the write gate (the api-access rule, step 11) each become a warning toast whose "Reload page" action loads the page as a new document, and a 401 or a cancellation shows nothing because the unauthorized handler already navigates once. Render `<ToastRegion/>` once inside the interactive component that uses the presenter; the toast is the in-house `ToastService`, never the component library's toast provider. Inside a modal dialog a toast sits behind the backdrop, so a dialog's form applies failures with its own `FormErrorMapper.ApplyFailure(result)` and renders `<FormErrorAlert Errors="_formErrors"/>` inside the dialog (`blazor/Blazor.Client/Users/ChangeUserRoleDialog.razor`).
6. Guard unsaved edits on an interactive surface:
   - Render `<UnsavedChangesGuard HasUnsavedChanges="..."/>` on a page with edits; it blocks navigation from .NET, in-app links, Back and Forward inside the document and document unload, and opens the "Unsaved changes" dialog with Stay and Leave
   - Use `DirtyDialog` for edits inside a dialog and route the cancel button through its `RequestCloseAsync`, so Escape, the close button, the backdrop and Cancel are guarded the same way
   - Subscribe to `AuthenticationNavigator.Leaving` to clear sensitive state; the guard releases itself on that event so a lost session never traps the user
   - Report the edit to the guard as it is made: `HasUnsavedChanges` is everything the guard knows, and `InputText` and the other `InputBase` components commit on `change`, which the browser raises only when the field loses focus. A click on a link blurs the field first and is therefore guarded either way, but a document unload while the edited field still has focus is not, because the edit has not reached .NET. Bind such a field with `@bind:event="oninput"` where a reload, a typed address or Back must be guarded on it
7. Keep API text as returned: messages render as text, never as markup, and stay English in every UI culture.

## Examples

### Example 1 - A Static Form That Stays in the Public Surface

```razor
@* ✅ DO: enhanced, named, bound from the form post, validated with data annotations, failures mapped and shown in the alert
   (blazor/Blazor.Host/Components/Pages/Public/Login.razor) *@
<EditForm EditContext="_editContext" FormName="login-start" OnValidSubmit="StartAsync" Enhance>
    <DataAnnotationsValidator/>
    <label>
        @CommonStrings.Email
        <InputText @bind-Value="Input.Email" type="email" autocomplete="email" data-testid="email"/>
    </label>
    <ValidationMessage For="() => Input.Email"/>
    <button type="submit" data-testid="submit">@AuthenticationStrings.LogInWithEmail</button>
</EditForm>

<FormErrorAlert Errors="_formErrors"/>

@code {

    [SupplyParameterFromForm(FormName = "login-start")]
    private EmailForm Input
    {
        get => field ??= new EmailForm();
        set;
    }

    protected override void OnInitialized()
    {
        _editContext = new EditContext(Input);
        _formErrors = new FormErrorMapper(_editContext);
    }

    private async Task StartAsync()
    {
        var result = await EmailAuthentication.StartLoginAsync(new StartEmailLoginCommand(Input.Email), HttpContext.RequestAborted);
        if (!result.IsSuccess)
        {
            _formErrors.ApplyFailure(result);
            return;
        }

        Navigation.NavigateTo(
        $"{AppUrls.ToAbsolute("login/verify")}?id={Uri.EscapeDataString(result.Value.EmailLoginId.Value)}&email={Uri.EscapeDataString(Input.Email)}&returnPath={Uri.EscapeDataString(AppUrls.SanitizeReturnPath(ReturnPath))}"
    );
    }

}
```

### Example 2 - A Static Form That Crosses the Authentication Boundary

```razor
@* ✅ DO: not enhanced, so success is a full document load with the session cookies; the redirect forces a new document
   (blazor/Blazor.Host/Components/Pages/Public/LoginVerify.razor) *@
<EditForm EditContext="_editContext" FormName="login-verify" OnValidSubmit="CompleteAsync">

@code {

    private async Task CompleteAsync()
    {
        if (!EmailLoginId.TryParse(Id, out var emailLoginId))
        {
            _formErrors.AddFormMessage(AuthenticationStrings.LoginExpired);
            return;
        }

        var result = await EmailAuthentication.CompleteLoginAsync(emailLoginId, new CompleteEmailLoginCommand(Input.OneTimePassword), HttpContext.RequestAborted);
        if (!result.IsSuccess)
        {
            _formErrors.ApplyFailure(result);
            return;
        }

        Navigation.NavigateTo(AppUrls.SanitizeReturnPath(ReturnPath), true);
    }

}

@* ❌ DON'T: an enhanced verification form; the runtime-less page would patch the DOM instead of loading the signed-in document *@
<EditForm EditContext="_editContext" FormName="login-verify" OnValidSubmit="CompleteAsync" Enhance>

@* ❌ DON'T: a toast or an interactive component on a public page; the surface has no WebAssembly *@
<ToastRegion/>
```

### Example 3 - An Interactive Form, Toast and Guard

```razor
@* ✅ DO: the same mapper on an interactive EditContext, the presenter for failures, the region inside the component
   and the guard on the page's edits (blazor/Blazor.Client/Development/FormErrorsInteractiveFixture.razor) *@
@inject ApiFailurePresenter FailurePresenter
@inject AuthenticationNavigator AuthenticationNavigator

<ToastRegion/>

<EditForm EditContext="_editContext" OnValidSubmit="Submit" data-testid="interactive-form">
    <DataAnnotationsValidator/>
    ...
</EditForm>

<UnsavedChangesGuard HasUnsavedChanges="IsPageDirty"/>

<DirtyDialog @ref="_dirtyDialog" Open="_isDialogOpen" OpenChanged="OnDialogOpenChanged" HasUnsavedChanges="_dialogDraft.Length > 0" Title="Edit the draft">
    <button type="button" @onclick="() => _dirtyDialog.RequestCloseAsync()">Cancel</button>
</DirtyDialog>

@code {

    protected override void OnInitialized()
    {
        _editContext = new EditContext(_model);
        _formErrors = new FormErrorMapper(_editContext);
        AuthenticationNavigator.Leaving += ClearSensitiveState;
    }

    private void SaveWithFailure()
    {
        var (outcome, problem) = FormErrorScenarios.Get(FormErrorScenarios.Detail);
        FailurePresenter.Present(outcome, problem);
    }

}
```

```csharp
// ❌ DON'T: decide the presentation in the component; the classifier already knows a 401 shows nothing
if (result.Problem?.StatusCode == 401) return;
toastService.Show(ToastKind.Error, "Error", result.Problem?.Detail ?? "Failed", "toast");
```
