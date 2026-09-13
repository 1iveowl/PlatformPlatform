# Blazor edition capability map

Written by the stage B3 spike (Linear EP-56). This map replaces the assessment's estimate of "10 to 15 components to
write or source". It prices behaviour, not component names.

- **Inventory:** verified at commit `adabcad3a` on 2026-09-13, against the React edition under `application/`.
- **Package evidence:** from the B3 users list (`blazor/Blazor.Client/Users/`) and the XML documentation shipped in the
  packages named below.
- **Evidence labels:** rows marked *tested* were exercised in B3 or B1 against the real API. Every other row is an
  assumption, drawn from the package documentation or the component list, and stage C or D re-checks it before
  building on it.

## Packages and versions

| Package | Version | Status |
| -- | -- | -- |
| `Microsoft.FluentUI.AspNetCore.Components` | 5.0.0-preview.26254.1 (nightly, dev-v5 commit `f3574653d`) | Prerelease. Ships `net8.0`, `net9.0`, `net10.0` and `net11.0` assets. |
| `Microsoft.AspNetCore.Components.QuickGrid` | 11.0.0-rc.1.26425.128 | Prerelease, part of ASP.NET Core 11 RC1 |
| `Blazor-ApexCharts` | 7.0.0 | Stable, `net10.0` assets. Tested in B1 under the nonce with a shim. |

## Scale used for effort

- **S:** up to half a day of adaptation or configuration.
- **M:** one to three days. This is a reusable wrapper or behaviour layer, written once and used on several surfaces.
- **L:** a week or more. Either a component written from scratch, or a behaviour with its own test surface.

Estimates cover one engineer and exclude surface work, strings and end-to-end flows, which the run plan puts in each
surface slice.

## Excluded: showcase-only components

The React design system has 84 components in `application/shared-webapp/ui/components/`. At `adabcad3a`, the components
below are imported only by the back-office component showcase (`BackOffice/routes/components/`), or by nothing at all.

- **Excluded from the map:** Breadcrumb, NavigationMenu, Resizable, AspectRatio, Collapsible, Accordion, ScrollArea,
  Sheet, Drawer, Command, SelectField, MultiSelect, Combobox, ComboboxField, CheckboxField, RadioGroupField, Slider,
  SliderField, InputOtpField, ButtonGroup, DatePicker, DateField, DateInput,
  TimeField, TimeZonePicker and Progress: 26 of the 84.
- **How this was checked:** at `adabcad3a`, an import scan found no importer of these components in `account/WebApp`,
  `main/WebApp` or `account/BackOffice` outside the showcase. Combobox, Slider and SelectField are imported inside
  `ui/components` only by other components on this list.
- **Why:** they are not product requirements.
- **Rule for the showcase itself:** it is not ported. If the Blazor edition wants a showcase, it gets one for the
  components it actually builds.

## Lists

| Capability (where it is used) | Selected component | Missing behaviour | Work |
| -- | -- | -- | -- |
| Server-paged list with numbered pages: accounts, back-office users, invoices, billing events, flag tenants and flag users, recycle bin, account detail tabs | `QuickGrid` with `ItemsProvider` and `Pagination`. *Tested* on users. | The paginator's "Page N of M" text is English and cannot be templated; only the summary can. Its links write `?page=` through NavigationManager (see the B3 record). A localized paginator is needed. | M |
| Sortable columns with `aria-sort`, server-side sort | `QuickGrid` `TemplateColumn` with `SortBy` and `IsDefaultSortColumn`. *Tested.* | Sort links write `?sort=` and `?direction=`, so the list's own URL state has to be reconciled with the grid's | S |
| Two lists on one page with prefixed URL parameters (flag detail) | `QuickGrid` `QueryParameterNameOptions` with a prefix. Assumption, from the documentation. | None expected | S |
| Infinite scroll on phones (users) | `QuickGrid` with `Virtualize`. *Tested* with 10,001 rows. | Server pages have to be mapped to index ranges. In B3 that is `UsersApiClient.cs`, 111 lines including the API calls. | S |
| Multi-select with checkboxes, select-all and indeterminate header (recycle bin, users) | No QuickGrid equivalent. *Tested:* a custom checkbox column. FluentDataGrid's `SelectColumn` has select-all and toggles on click and Enter, but not on Space (B3 record). | Select-all, indeterminate state, Shift-click range, Cmd or Ctrl-click toggle, and clearing the selection on page, sort or filter change | M |
| Row keyboard model: roving tab stop, arrows, Home and End, Enter to activate, Space to toggle, activate-on-navigate (accounts preview), scroll to key | Custom JavaScript module. *Tested:* arrows, Space and Enter in `users-grid.js` (74 lines). | Roving `tabindex`, Shift+Arrow range, activate-on-navigate, scroll-to-key without moving focus, and re-checking the focused key after data changes | M |
| Row click or Enter navigates or opens the pane. Invoice, billing event and flag rows are mouse-only in React, a defect not to carry over. | `QuickGrid` `OnRowClick` on .NET 11, plus the keyboard layer. Assumption. | Nothing beyond the keyboard layer | S |
| Kebab row menu, and long-press context menu on phones | `FluentMenu`, `FluentMenuList` and `FluentMenuItem` with `Trigger`. *Tested* on desktop. | The items must sit inside `FluentMenuList`, or every item renders visibly. Long-press is not provided (`OpenOnContext` is right-click only; assumption). One menu per row is rendered. | M |
| Side pane: docked from 48rem, full screen with focus trap, scroll lock and focus return below it; closes on Escape and on path change | Custom layout region. *Tested* docked, with Escape and focus return to the row. `FluentDialog` with `Alignment` End is the candidate for the full-screen case (assumption). | The narrow full-screen mode, scroll lock and close on navigation | M |
| Loading skeleton and empty state (every list) | `FluentSkeleton`, plus a region outside the grid. Assumption. | QuickGrid has `PlaceholderTemplate` for virtualized rows only, and no empty-state slot | S |
| Responsive column hiding at breakpoints (accounts, recycle bin) | Column `Class` plus CSS in a global stylesheet. Assumption. | Nothing; not tested | S |
| Search debounced by 500ms, cleared with Escape | `FluentTextInput` with `Immediate` and `ImmediateDelay="500"`. *Tested.* | Escape to clear | S |
| Enum select with an "Any" option (users) | `FluentSelect`. *Tested.* | None | S |
| Multi-select toggle groups for plan, status, role and activity (accounts, back-office users, flag sections) | No v5 ToggleGroup in the component list. A group of `FluentToggleButton` is the candidate (assumption). | Group semantics, single or multiple selection mode, and arrow-key movement | M |
| Date range filter (users, modified date) | No range picker in v5. B3 used two native date inputs. `FluentDatePicker<T>` handles a single date (assumption). | A range picker with one popover, or an accepted two-field design | M |
| Filter dialog below 54rem, inline filters above it, active-count badge, "Clear filters" | `FluentDialog` and `FluentBadge`. *Tested:* dialog, count and clear. | The switch between inline and dialog driven by width (ResizeObserver interop) | S |
| Removable filter badges (accounts drift banners) | `FluentBadge` with a dismiss button. Assumption. | None | S |
| View toggle that maps to status sets (invoices, billing events) | The toggle group row above | Covered there | – |
| Optimistic switch with rollback, warning colour and tooltip (flag overrides) | `FluentSwitch` and `FluentTooltip`. Assumption. | The rollback and delayed-refetch logic | S |
| Number field that saves on change (rollout percentage) | `FluentNumberInput<T>`. Assumption. | None | S |
| Tabs held in `?tab=` (account and user detail) | `FluentTabs`, with URL sync. Assumption. | The same URL-state helper as the lists | S |
| Charts: area, bar, line and pie, with a period toggle (back-office dashboard) | `Blazor-ApexCharts` 7.0.0. *Tested* in B1 under the nonce with a shim. | The nonce shim, and five chart cards | M |

## Forms and dialogs

| Capability (where it is used) | Selected component | Missing behaviour | Work |
| -- | -- | -- | -- |
| Server validation: ProblemDetails `errors` mapped to fields, kept until the next submit (every form) | `EditForm`, `EditContext` and `ValidationMessageStore`, with `FluentValidationMessage`. Assumption. | A shared ProblemDetails-to-`ValidationMessageStore` mapper, and a toast for non-validation errors | M |
| Client validation (email, required) | `DataAnnotationsValidator`. Tested on static SSR in B2; WebAssembly is an assumption. | None | S |
| Confirmation dialogs: delete one or many users, purge, empty bin, revoke session, delete flag | `FluentDialog`. *Tested* on delete one, delete many and role change. | Focus lands on the dialog element, not the first control; the React dialogs autofocus the first radio or button | S |
| Unsaved-changes guard on dialogs and pages (settings, profile, invite, role, billing info, A/B pin) | `NavigationLock` plus `FluentDialog`. Assumption. | A shared dirty-state wrapper for dialogs and for page navigation | M |
| Radio group with descriptions (change role, cancel reason, A/B pin) | `FluentRadioGroup` and `FluentRadio`. *Tested* for the role change. | Per-option descriptions (not checked) | S |
| Focus return after a dialog closes | `FluentDialog`. *Tested:* focus returned to the row's action button, the element that opened the menu. | None on desktop | S |
| Logo and avatar upload: drop zone plus file picker, jpeg, png, gif and webp up to 1 MB, no cropping | `FluentInputFile`. Assumption. | Type and size validation messages localized, and preview | M |
| One-time password: 6 slots, upper case, auto-submit at 6, countdown with `aria-live`, resend, refocus after an error | None in v5 | Written from scratch, with a static SSR variant (B2 uses a plain input) | L |
| Toasts: 47 production calls, plus global HTTP error toasts | `FluentToast` through its provider. Assumption. | A global HTTP-error-to-toast bridge | S |
| Tooltip with tap support | `FluentTooltip`. Assumption. | Tap behaviour on touch (not checked) | S |
| Preferences: theme, language and zoom, saved immediately | Toggle group (above), a theme service and a CSS variable for zoom. Assumption. | Covered by the toggle group | S |
| Country select (billing info) | `FluentCombobox` or `FluentSelect`. Assumption. | A localized country list | S |
| Stripe Elements payment forms (checkout, update payment method, retry) | JavaScript interop, or hosted Checkout per stage F. Excluded here, decided by F1. | – | – |
| Markdown legal pages | Rendered on the server on the static SSR surface. Assumption. | The markdown renderer choice | S |
| Relative dates (SmartDate) | A helper using `CultureInfo`. Assumption. | Written from scratch | S |
| Avatar with initials, tenant logo | `FluentAvatar` and `FluentImage`. Assumption. | None | S |
| App shell: sidebar, navigation, mobile menu and banners | `FluentLayout`, `FluentNav` and `FluentLayoutHamburger`. Assumption. | Tenant switcher, user menu, banner slot | M |
| Localization of en-US and da-DK, about 1,600 messages | `IStringLocalizer` resources, plus FluentUI's localizer for built-in texts. Assumption. | Built-in QuickGrid texts cannot be localized (see paginator). FluentUI's built-in texts go through its localizer (not checked). | M, plus strings per slice |
| Install to home screen (PWA) | Custom | Stage E | – |

## Count

Behaviours that need writing or sourcing, rather than configuring a component:

- **L:** one. The one-time password input.
- **M:** 13. Localized paginator, selection model, row keyboard model, row menus with long-press, responsive side pane,
  toggle group, date range, ProblemDetails validation mapping, dirty-state guard, upload wrapper, chart cards, app shell,
  and localization of built-in texts.
- **S:** about 25 configuration or adaptation items.

Rough total: 1 L (about a week), 13 M (13 to 39 days) and 25 S (up to 12 days). That is 30 to 60 engineer-days of
component and behaviour work, before surface slices. It does not include adopting FluentDataGrid (see the B3 record) or
the Stripe forms.

This does not map one to one onto "components to write". Several M items are behaviour layers over existing components
(keyboard, selection, dirty state) rather than new components. Compared with the assessment's 10 to 15 components:

- **Consolidated or excluded:** the 84 design-system entries shrink once 26 showcase-only components are excluded.
- **Covered by FluentUI or QuickGrid with configuration:** most of the remainder (the S rows).
- **Still work:** 14 items, the M and L rows.

That lands in the assessment's range by count, but the M items are costlier than "a component each". Stage C or D will
re-check the assumption rows first: date range, toggle group, long-press, upload and one-time password.
