---
paths: **/routes/**/*.tsx,**/router/**,**/federated-modules/**
description: Rules for navigation, routing and route protection with the shared TanStack Router
---

# Navigation

Rules for navigating within and between self-contained systems and for protecting routes.

## Implementation

1. Follow these navigation rules:
   - **One router per page**: The Main host runs the only TanStack Router. Federated systems contribute route subtrees to it (account exposes `./routes`; Main grafts it in `shared/lib/router/router.tsx`). Never create a second router in a federated module - lint blocks `createRouter` outside `shared/lib/router/router.tsx`
   - **The router owns browser history**: never call `window.history.pushState`/`replaceState` - lint blocks both. Hand-rolled history sync is a shadow routing pattern that desynchronizes the router from the address bar
   - **Within a self-contained system**: Use `useNavigate()` hook or `<Link>` component from TanStack Router
   - **Account to Main**: Use the `useMainNavigation()` hook. Main paths such as `/dashboard` are ordinary navigations on the shared router; they are just not part of account's typed route tree
   - **To Back-Office**: Back-Office is a separate SPA, so use `window.location.href` for full-page navigation
   - Only use `window.location.href` when navigating to a different SPA or for full-page reloads (e.g., logout)
   - **Route protection**: use `beforeLoad` only with the `routeGuards` helpers (`requireAuthentication`, `requirePermission`, `requireSubscriptionEnabled`) or tiny context flags like `disableAuthSync`. Prefer declarative guards that render `<Navigate>` (e.g. `OnboardingGuard`) for user-state redirects. Never load data in `beforeLoad` or route loaders - server state lives in TanStack Query only
