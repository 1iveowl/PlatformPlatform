---
paths: application/shared-webapp/ui/components/**
description: Rules for installing and adapting ShadCN 2.0 components in the shared UI library
---

# ShadCN Components

Rules for adding, reinstalling and styling the shared ShadCN 2.0 components in `@repo/ui`.

## Implementation

1. Install and adapt components:
   - Install components via `npx shadcn add <component>` - never copy manually
   - After installing: change `@/utils` to `../utils` and rename file to PascalCase (e.g., `button.tsx` to `Button.tsx`)
   - **Divergences**: Tracked in `application/shared-webapp/ui/components/README.md` (Component Inventory table + Global Divergence Patterns section). Do not add inline `// NOTE: This diverges from stock ShadCN ...` comments. When reinstalling a component, consult its inventory row and reapply the listed divergences.

2. Apply the shared styling divergences:
   - **Focus ring**: Replace ShadCN's default `focus-visible:ring-*` / `focus-visible:border-ring` utilities with `outline-ring focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2`. Use `outline-primary` or `outline-destructive` for colored variants instead of `outline-ring`
   - **Apple HIG compliance**: Interactive controls must use CSS variable heights: `h-[var(--control-height)]` (44px default), `h-[var(--control-height-sm)]` (36px), `h-[var(--control-height-xs)]` (28px). For controls like checkboxes/switches where 44px visual size is too large, ensure a 44px minimum tap target using `after:absolute after:-inset-3`
   - **Cursor pointer**: Replace `cursor-default` with `cursor-pointer` on clickable elements
   - **Active state feedback**: Add press feedback to interactive elements using `active:` pseudo-class with background color changes. Buttons/triggers: `active:bg-primary/70` (or variant-specific active backgrounds). Menu/list items: `active:bg-accent`. Small controls (checkbox, radio): `active:border-primary`
