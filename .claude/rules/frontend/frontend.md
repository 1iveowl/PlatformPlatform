---
paths: **/*.tsx,**/*.ts,**/package.json,**/tsconfig.json,**/*.esproj
description: Core rules for frontend TypeScript and React development
---

# Frontend

Guidelines for frontend TypeScript and React development, including architecture, code style, and build/format steps. Component, dialog, navigation and shared ShadCN component rules live in [react-components](/.claude/rules/frontend/react-components.md), [dialogs](/.claude/rules/frontend/dialogs.md), [navigation](/.claude/rules/frontend/navigation.md) and [shadcn-components](/.claude/rules/frontend/shadcn-components.md); read the matching one before changing a dialog or navigation outside the files those rules are scoped to.

## Code Navigation

Use LSP tools aggressively for code investigation: `goToDefinition`, `findReferences`, `hover`, `documentSymbol`. If LSP returns "No LSP server available", stop and instruct the user: `npm install -g typescript-language-server typescript`

## Browser Testing

Use browser MCP tools to test at `https://app.dev.localhost:<appGateway>`. Look up the `appGateway` port via the Aspire MCP `list_resources` tool, or read `.workspace/port.txt` for the base port (the gateway runs on the base port itself). Use `UNLOCK` as OTP verification code (localhost only).

## Architecture Overview

1. **SPA Served by .NET Backend**:
   - SPA served via `SinglePageAppFallbackExtensions.cs` from the backend
   - UserInfo injected into HTML meta tags and available via `import.meta.user_info_env`
   - Authentication is server-side with HTTP-only cookies
   - YARP reverse proxy handles routing between SPA and APIs

2. **Module Federation for Micro-Frontends**:
   - Each self-contained system has its own WebApp
   - Common UI exposed via federation in `federated-modules/`
   - **Federated leaf exposes stay presentation-shaped**: they may navigate through the shared router hooks, but they never own routes, history, or shared DOM regions. The only structural expose is `./routes` (the route subtree contribution)
   - Shared components in `application/shared-webapp/`
   - Don't import directly between self-contained systems

3. **API Integration**:
   - API client auto-generated from OpenAPI spec
   - Located in `shared/lib/api/client.ts`
   - A SPA only calls its own self-contained system's endpoints via that system's generated OpenAPI client. Prefixes: `main` → `/api/*`, `account` (user-facing app) → `/api/account/*`, `account` (back-office surface) → `/api/back-office/*`. Cross-system needs go backend-to-backend via `/internal-api/...`, exposed to the SPA through a facade endpoint in its own system
   - Never make direct fetch calls
   - Server state lives in TanStack Query only
   - Use `queryClient.invalidateQueries()` to refresh data after mutations

## Implementation

1. Follow these code style and pattern conventions:
   - Use proper naming conventions:
     - PascalCase for components (e.g., `UserProfile`, `NavigationMenu`)
     - camelCase for variables and functions (e.g., `userName`, `handleSubmit`)
   - Create semantically correct components with clear boundaries and responsibilities:
     - Each component should have a single, well-defined purpose
     - UI elements with different functionality should be in separate components
     - Avoid mixing unrelated functionality in one component
   - Use clear, descriptive names instead of making comments
   - Don't use acronyms (e.g., use `errorMessage` not `errMsg`, `button` not `btn`, `authentication` not `auth`)
   - Prioritize code readability and maintainability
   - Don't introduce new npm dependencies
   - **No barrel export files**: Do not create `index.ts` files that only re-export components from a folder. Import each component directly from its file (e.g., `import { Button } from "../components/Button"` not `import { Button } from "../components"`)
   - **Workspace package imports**: Within the same package (e.g., within `@repo/ui`), use relative imports (`../components/Button`) to avoid circular dependencies. Between different packages, use absolute imports (`@repo/ui/components/Button`)

2. Always follow these steps when implementing changes:
   - Consult relevant rule files and list which ones guided your implementation
   - Search the codebase for similar code before implementing new code
   - Reference existing implementations to maintain consistency

3. Build and format your changes:
   - After each minor change, use the **build** skill (`--frontend --quiet`)
   - This ensures consistent code style across the codebase

4. Verify your changes:
   - When a feature is complete, run **build**, then **format** and **lint** in parallel (with `--no-build`), all scoped with `--frontend --quiet`
   - **ALL lint findings are blocking** - CI pipeline fails on any result marked "Issues found"
   - Severity level (note/warning/error) is irrelevant - fix all findings before proceeding
   - Fix any compiler warnings or test failures before proceeding
