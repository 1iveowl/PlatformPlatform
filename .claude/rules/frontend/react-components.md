---
paths: **/*.tsx
description: Rules for React components, ShadCN 2.0 with BaseUI usage, layout, errors, and UI telemetry
---

# React Components

Rules for writing React components with ShadCN 2.0 on BaseUI. Building or changing a dialog: read [dialogs](/.claude/rules/frontend/dialogs.md). Navigation: read [navigation](/.claude/rules/frontend/navigation.md).

## Implementation

1. **ShadCN 2.0 with BaseUI** (not Radix UI):
   - **BaseUI** (`@base-ui/react`): Headless primitives providing accessibility and behavior
   - **ShadCN 2.0**: Pre-styled components built on BaseUI, using class-variance-authority (cva)
   - **Never use `*:` or `**:` variants**: These Tailwind child/descendant variants generate `:is()` CSS selectors that the module federation CSS scoping plugin cannot scope, causing specificity bugs in production. In shared components, use `[&>*]:` or `[&_*]:` selectors instead. In application code, prefer putting the utility class directly on each child element (e.g., `className="max-sm:grow"` on each button instead of `max-sm:*:grow` on the parent)
   - Import from `@repo/ui/components/`, never from BaseUI directly
   - Only create custom components when no ShadCN equivalent exists (edge cases)
   - **Icon-only buttons**: Must have a `Tooltip` wrapper. Use descriptive labels, e.g., "Account settings" not "Settings", "Log out" not "Logout"
   - **Use BaseUI `render` prop** to customize underlying elements (not Radix's `asChild`): `<DialogClose render={<Button />}>Close</DialogClose>`
   - **Charts**: Import all chart primitives from `@repo/ui/components/Chart` — direct `recharts` imports are lint-blocked. Wrappers default `accessibilityLayer={true}` so Tab focuses data points, not the SVG (otherwise Chrome paints an unstyleable blue ring)

2. Use these element and styling conventions:
   - Use ShadCN components instead of native HTML elements like `<a>`, `<button>`, `<fieldset>`, `<form>`, `<input>`, `<label>`, `<ol>`, `<p>`, `<progress>`, `<select>`, `<table>`, `<textarea>`, `<ul>` (native `<div>`, `<span>`, `<section>`, `<article>`, `<img>`, `<h1>`-`<h4>` are acceptable)
   - **Headings**: Use native `<h1>`-`<h4>` elements with global styles from `tailwind.css`. Never override font sizes or weights - use the correct semantic level for the visual hierarchy. Allowed overrides: alignment (`text-center`), margins (`mb-X`), visibility (`hidden sm:block`). Exception: Hero/marketing pages can override sizes
   - Use native `<img>` for images. Keep it simple for small logos/icons. For large images:
     - **LCP images** (large hero images): Add `fetchPriority="high"`
     - **Below-the-fold images**: Add `loading="lazy"`
     - Never use `width`/`height` HTML attributes. Use Tailwind `size-*` classes instead
     - Always include localized `alt` text using the `t` macro (e.g., `alt={t\`Description\`}`)
   - **Square dimensions**: Use Tailwind's `size-N` utility instead of `h-N w-N` for any square element (e.g., `size-4` not `h-4 w-4`). Only use separate `h-N w-N` for rectangular dimensions
   - **Rem-based sizing**: All sizes must use `rem`, never `px`. This enables UI scaling via the `--zoom-level` CSS variable while maintaining aspect ratios.
     - Tailwind arbitrary values: `max-w-[25rem]` not `max-w-[400px]`, `ml-[0.375rem]` not `ml-[6px]`
     - CSS variable fallbacks: `var(--banner-height,0rem)` not `var(--banner-height,0px)`
     - JS constants for CSS: `const BANNER_HEIGHT = "3rem"` not `const BANNER_HEIGHT = 48`
     - For JS pixel calculations, use `getSideMenuCollapsedWidth()` from `@repo/ui/utils/responsive`

3. Use the following React patterns and libraries:
   - Use ShadCN components from `@repo/ui/components/ComponentName`:
     - Search [Components](/application/shared-webapp/ui/components) when you need to find a component
     - Use existing components rather than creating new ones
   - Use `onClick` for click handlers and `disabled` for disabled state (ShadCN patterns)
   - Use `<Trans>...</Trans>` for JSX translations, `t` macro for strings
   - Use TanStack Query for API interactions via `api.useQuery()` and `api.useMutation()`
   - Don't use `fetch` directly - use the generated API client
   - Use Suspense boundaries with error boundaries at route level
   - Colocate state with components - don't lift state unnecessarily
   - Use `useCallback` and `useMemo` only for proven performance issues
   - Throw errors sparingly and ensure error messages include a period
   - Include appropriate aria labels for accessibility (e.g., `slot="title"` on Heading in dialogs)
   - Disable UI during pending operations: `disabled={mutation.isPending}` on buttons/fields, `isDismissable={!mutation.isPending}` on modals

4. Error handling:
   - **Errors are handled globally** - `shared-webapp/infrastructure/http/errorHandler.ts` automatically shows toast notifications with the server's error message (don't manually show toasts for errors)
   - **Validation errors**: Pass to forms via `validationErrors={mutation.error?.errors}`
   - **`onError` is for UI cleanup only** (resetting loading states, closing dialogs), not for showing errors
   - **Toast notifications**: Show success toasts in mutation `onSuccess` callbacks, not in `useEffect` watching `isSuccess` (avoids React effect scheduling delays)

5. Responsive design utilities:
   - Use `useViewportResize()` hook to detect mobile viewport (returns `true` when mobile)
   - Use `isTouchDevice()` for touch vs mouse interactions
   - Use `isMediumViewportOrLarger()` for desktop-specific features

6. Z-index layering (don't invent new values):
   - `z-0` to `z-10`: **Content**: sticky table headers, sticky toolbars, inline badges, calendar layers
   - `z-20`: **App bars**: desktop top bar, mobile floating menu button
   - `z-30`: **Navigation + mobile header**: side menu, mobile sticky header (animates below banners)
   - `z-[35]`: **Backdrops**: dimmed overlays behind panels and overlay-mode navigation
   - `z-40`: **Banners + panels**: banner container (above mobile header), side panes, mobile full-screen menus, side menu in overlay mode
   - `z-50`: **Popups**: dialogs, dropdowns, popovers, tooltips (ShadCN default)
   - `z-[60]`: **Toasts**: always visible, even above dialogs
   - `z-[99]`: **Critical**: full-screen loaders, system overlays (e.g., account switching)
   - `z-100`: **Select popup**: Select dropdown renders above dialogs (ShadCN default)

7. **Telemetry tracking** (all tracked as `trackPageView`, not custom events): `Dialog`, `AlertDialog`, `SidePane`, and `TablePagination` require a `trackingTitle` prop (e.g., `trackingTitle="Invite user"`). `DropdownMenu` accepts an optional `trackingTitle` prop for menu open/close tracking, and `DropdownMenuItem` accepts an optional `trackingLabel` prop for item selection tracking (e.g., `trackingLabel="Upload logo"`). For custom interactions, use `trackInteraction(name, type, action)` from `ApplicationInsightsProvider`. This also emits a page view, not a custom event. Route-level page tracking uses `staticData: { trackingTitle: "Page name" }` in route definitions

8. **Empty states**: Use the `Empty` component with icon, title, and description when there is no content to display

9. **Loading states**: Use the `Skeleton` component to show placeholder UI while content is loading instead of spinners

## Examples

```tsx
// ✅ DO: Rem-based sizing
const BANNER_HEIGHT = "3rem";
document.documentElement.style.setProperty("--banner-height", BANNER_HEIGHT);
document.documentElement.style.setProperty("--banner-height", "0rem"); // cleanup
<div className="max-w-[25rem] ml-[0.375rem]" />
<div className="pt-[calc(1rem+var(--banner-height,0rem))]" />

// ❌ DON'T: Px-based sizing
const BANNER_HEIGHT = 48;
document.documentElement.style.setProperty("--banner-height", `${BANNER_HEIGHT}px`);
document.documentElement.style.setProperty("--banner-height", "0px"); // cleanup
<div className="max-w-[400px] ml-[6px]" />
<div className="pt-[calc(1rem+var(--banner-height,0px))]" />
```
