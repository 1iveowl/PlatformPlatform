---
paths: blazor/**/*.razor,blazor/Directory.Packages.props,blazor/**/*.csproj,blazor/nuget.config
description: Rules for using the FluentUI component library in Blazor, its menus, its localizer, the components to avoid under the style policy and the pinned prerelease version
---

# Component Library

FluentUI Blazor (`Microsoft.FluentUI.AspNetCore.Components`) is the component foundation of the Blazor edition, registered in both containers with the shared localizer. Components that write style attributes are replaced by in-house ones because the host's policy has no `style-src-attr`. The package is pinned to a nightly build that the package upgrade workflow must not move.

## Implementation

1. Register the library on both sides with `AddFluentUIComponents(configuration => configuration.Localizer = new FluentResourceLocalizer())`, in `Blazor.Host/HostApplication.cs` for static rendering and prerendering and in `Blazor.Client/Program.cs` for the browser. A public page may use a FluentUI component; it resolves its services from the host container.
2. Do not use `FluentMenu` for row actions: it positions its popover by writing `anchor-name` and `position-anchor` style attributes on the trigger and the list. The policy does not report a violation for a style set from script, but the edition allows no style attribute; use `DataListRowMenu` (see the lists rule). Where a `FluentMenu` is used outside a list, its items go inside a `FluentMenuList`; a `FluentMenuItem` outside `FluentMenuList` does not render as a menu.
3. Use the in-house components where the library one writes a style attribute: `ToastService` and `ToastRegion` instead of the toast provider, `ModalDialog` and `DirtyDialog` on the native `<dialog>` instead of the library dialog, `DataList` on QuickGrid instead of the data grid, and `DataListRowMenu` instead of the library menu. Before using a component new to the edition, render it in a harness case and check for `securitypolicyviolation` events and for style attributes in the document body; a component that violates the policy is wrapped or avoided, never the policy widened.
4. Keep the FluentUI pin exactly as `Directory.Packages.props` states: `5.0.0-preview.26254.1`, a nightly built from the library's dev-v5 branch and restored from the feed mapped in `blazor/nuget.config`. SemVer orders it below `5.0.0-rc.5-26219.1`, which throws two custom event registration errors on every load under .NET 11 RC1, so any "upgrade to latest" would pick the broken release. The `update-packages` command rewrites every `Directory.Packages.props` in the repository, `blazor/` included, and the package is not in its `RestrictedNuGetPackages` list, so the upgrade-packages skill always passes `--exclude Microsoft.FluentUI.AspNetCore.Components`. The exit criterion is a nuget.org release that contains upstream commit c813a4a6b: then pin that release and remove the feed and its source mapping. The NU3042, NU3018 and NU3027 restore warnings for the nightly's test signature stay visible.
5. Keep the host page's `no-fuib-style` attribute on `<html>` and the absolute link to `default-fuib.css`; without it the library's initializer fetches the stylesheet relative to the document URL and gets 404 below the path base.
6. Keep `ILLink.Descriptors.xml` in the client project; it preserves `OverflowChangedItem`, the element type of the library's overflow event arguments, whose constructor the trimmer removes so a trimmed publish throws on the event. Add a type to it only when the trimmed smoke test proves the trimmer removed it, and delete an entry once the library preserves the type itself.
7. Match text inside a FluentUI component by containment in tests (`toContainText`); its shadow root adds slots the exact text match does not see.

## Examples

### Example 1 - A Row Menu

```razor
@* ✅ DO: the in-house row menu, which writes no style attribute (blazor/Blazor.Client/Users/UsersSurface.razor) *@
private RenderFragment<UserDetails> RowMenu => user =>
    @<DataListRowMenu Label="@UsersStrings.UserActions" Items="RowMenuItems(user)" TestId="user-actions"/>;

@* ❌ DON'T: the library menu in a row; it writes anchor positioning style attributes on the trigger and the list *@
<FluentMenu Trigger="@($"user-actions-{user.Id}")">
    <FluentMenuList>
        <FluentMenuItem OnClick="@(() => ViewProfileAsync(user))">@UsersStrings.ViewProfile</FluentMenuItem>
    </FluentMenuList>
</FluentMenu>

@* ❌ DON'T: items directly under FluentMenu *@
<FluentMenu Trigger="actions">
    <FluentMenuItem>View profile</FluentMenuItem>
</FluentMenu>

@* ❌ DON'T: the library's toast provider or dialog; both write style attributes the policy blocks *@
<FluentToastProvider/>
<FluentDialog @bind-Hidden="_hidden">...</FluentDialog>
```

### Example 2 - The Pin

```xml
<!-- ✅ DO: the pinned nightly with its reason and exit criterion (blazor/Directory.Packages.props) -->
<!-- Pinned to a nightly built from dev-v5 commit f3574653d and restored from the feed mapped in nuget.config. It carries
     upstream commit c813a4a6b, without which 5.0.0-rc.5-26219.1 throws two custom event registration errors on every
     load under .NET 11 RC1. SemVer orders this preview below 5.0.0-rc.5-26219.1, so the upgrade-packages skill and
     any "upgrade to latest" must not move it. -->
<PackageVersion Include="Microsoft.FluentUI.AspNetCore.Components" Version="5.0.0-preview.26254.1" />

<!-- ❌ DON'T: the "latest" prerelease; it is the broken release the pin avoids -->
<PackageVersion Include="Microsoft.FluentUI.AspNetCore.Components" Version="5.0.0-rc.5-26219.1" />
```
