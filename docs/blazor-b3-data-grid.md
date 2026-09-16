# B3: data grid spike record

This is the record of stage B3 of the Blazor edition (Linear EP-56). It compares FluentDataGrid and QuickGrid on the
real users list, on .NET 11, under the proposed Content Security Policy. The capability-to-component mapping for the
full edition is in [blazor-capability-map.md](blazor-capability-map.md).

## What was tested

- **When and where:** measured 2026-09-13 on `experiment/06-blazor`, on top of `adabcad3a`, with the change set this
  record is committed with.
- **Host:** the Aspire Development host from B1 (Debug build, not a trimmed publish), reached through the gateway at
  `https://app.dev.localhost:9000/blazor/`. Only the `blazor-host` resource was restarted; no second host was started.
- **SDK and runtime:** SDK 11.0.100-rc.1.26425.128, ASP.NET Core 11.0.0-rc.1.26425.128.
- **Packages:**

| Package | Version |
| -- | -- |
| `Microsoft.FluentUI.AspNetCore.Components` | 5.0.0-preview.26254.1 (nightly from B1; the package also ships a `net11.0` asset) |
| `Microsoft.AspNetCore.Components.QuickGrid` | 11.0.0-rc.1.26425.128, added by this spike |

- **Style policy:** `style-src <hosts> 'nonce-N'` and `style-src-elem <hosts> 'nonce-N'`, with no `unsafe-inline` and
  no `style-src-attr` directive, so style attributes fall back to `style-src`. This is the B1 policy, unchanged.
- **Comparison variant:** a second run adds `style-src-attr 'unsafe-inline'`. It is selected with the spike-only query
  parameter `csp-variant=style-attr-unsafe-inline`.
- **.NET 10:** not run. The repository decision (EP-52) is .NET 11, so the .NET 10 workaround the issue describes does
  not apply.
- **Browsers:** Chromium 149.0.7827.0, Firefox 151.0 and Playwright WebKit 26.5 (Playwright 1.61.0), headless,
  1440×900. Real Safari was not run.
- **Device:** Apple silicon inside a linuxkit VM, as in B1.
- **Harness:** `blazor/spike/b3-data-grid/grid.mjs`, with three modes:
  - `seed`
  - `measure` (optional `--csp-variant`)
  - `interact`

  Raw results are git-ignored in `.workspace/experiment-06-blazor/b3-results/`.

## Tenant size

The largest tenant size this spike commits the product to is 10,000 users.

- **No limit in the code today:** verified at `adabcad3a`, the account system has no user or seat limit, and the users
  API caps one page at 1,000.
- **The number itself is a judgement:** 10,000 is well above any tenant the React edition's list design (25 per page)
  was built for.

The tenant was seeded through the real API; nothing was written to the database directly.

- **Owner:** signed up.
- **Tenant:** named.
- **Invites:** 10,000 users (256 s, 8 in parallel, 0 failures).
- **Admins:** 150 promoted.
- **Active users:** 60 invitees logged in and set names.
- **Final count:** 10,001 users, 61 active.

Repeated interaction runs deleted users, so later runs saw 9,93x to 9,99x. Most seeded users have no name, which makes
sort by name less representative than sort by email.

## The users list

Both pages mirror the React users page:

- a debounced search;
- a filter dialog for role, status and modified date range, with an active-count badge and "Clear filters";
- server-side sort from the column headers;
- infinite scroll (virtualized) or numbered pages;
- multi-select with bulk delete;
- a row menu with View profile, Change role and Delete (owner only, disabled for your own row);
- a docked side pane driven by `?userId=`;
- URL state in the same parameters as React.

The routes are `/blazor/app/users/fluent` and `/blazor/app/users/quick`. They share the surface, API client, page cache
and dialogs, so only the grid component differs (`blazor/Blazor.Client/Users/`).

## Results under the nonce-only style policy

### CSP violations

| | FluentDataGrid | QuickGrid |
| -- | -- | -- |
| At first render (virtualized, 20 rows) | 177 to 225 `style-src-attr` | 0 |
| One measure session per mode (load, scroll or next page, search, URL filter, header sort) | 428 to 434 `style-src-attr` | 0, or 1 `style-src-elem` (Chromium only) |
| One interaction session | 301 `style-src-attr` | 0 |

- **Source of the FluentDataGrid violations:** its own markup. Style attributes on `table`, `th`, the header `div` and
  the icon `svg`, plus cell widths and heights.
- **Blocked styles change the layout:** rows render about 37 px high instead of the configured 56 px, and column widths
  are ignored (screenshots compared in Chromium).
- **Virtualize is compliant on .NET 11:** the spacer rows carry `data-blazor-virtualize-reserved-height` and get their
  height from script without a violation, in both grids.
- **The QuickGrid `style-src-elem` violation:** Chromium logs it on an enhanced navigation. It is the B2 finding: the
  brand `<style>` block is re-inserted without a nonce.

### Load, paging, search and sort

| Measure | Browser | FluentDataGrid | QuickGrid |
| -- | -- | -- | -- |
| First rows, virtualized; includes the WebAssembly boot | Chromium | 1,273 ms | 1,102 ms |
| | Firefox | 3,340 ms | 3,762 ms |
| | WebKit | 1,549 ms | 1,278 ms |
| Next page, paged | Chromium | 294 ms | 168 ms |
| | Firefox | 1,027 ms | 497 ms |
| | WebKit | 240 ms | 239 ms |
| Search to rows ready (500 ms debounce included) | Chromium | 776 ms | 764 ms |
| | Firefox | 1,158 ms | 1,039 ms |
| | WebKit | 689 ms | 751 ms |
| Header sort to rows ready | Chromium | 272 ms | 215 ms |
| | Firefox | 722 ms | 434 ms |
| | WebKit | 244 ms | 267 ms |
| Elements inside the grid, virtualized, first render | all | 465 | 400 |
| Rows rendered, virtualized | all | 20 (26 at the end) | 20 |

- **Samples:** one per browser, grid and mode, not a median.
- **Account API:** the median GET was 12 to 57 ms.

### Scrolling to row 10,001

**Method:** the harness scrolls the grid by three viewports every 50 ms until the last row renders. It counts
requestAnimationFrame gaps as it goes.

**Why time per step is the comparison:** rows are shorter in FluentDataGrid under the policy, so it needs fewer steps.
The time for each step is what is comparable.

| Browser | Grid | Policy | Steps | Time for each step | Frame gap p50 / p95 / max | Users GET requests |
| -- | -- | -- | -- | -- | -- | -- |
| Chromium | FluentDataGrid | nonce only | 197 | 113 ms | 17 / 117 / 122 ms | 100 |
| Chromium | FluentDataGrid | `style-src-attr 'unsafe-inline'` | 296 | 111 ms | 35 / 109 / 127 ms | 100 |
| Chromium | QuickGrid | nonce only | 300 | 52 ms | 17 / 22 / 82 ms | 2 |
| Firefox | FluentDataGrid | nonce only | 197 | 265 ms | 135 / 254 / 309 ms | 100 |
| Firefox | FluentDataGrid | `style-src-attr 'unsafe-inline'` | 296 | 295 ms | 110 / 202 / 228 ms | 100 |
| Firefox | QuickGrid | nonce only | 299 | 53 ms | 17 / 22 / 137 ms | 2 |
| WebKit | FluentDataGrid | nonce only | 196 | 97 ms | 65 / 140 / 164 ms | 100 |
| WebKit | FluentDataGrid | `style-src-attr 'unsafe-inline'` | 295 | 97 ms | 42 / 90 / 120 ms | 100 |
| WebKit | QuickGrid | nonce only | 299 | 52 ms | 18 / 44 / 56 ms | 2 |

- **Both grids reach the last row in every browser.**
- **Requests during the scroll:**
  - QuickGrid's Virtualize requests only the ranges it lands on (2 server pages).
  - FluentDataGrid requests every range it passes (all 100 server pages of 100).
- **FluentDataGrid is slow with or without the violations:** allowing style attributes removed them and restored the
  layout, but it did not make scrolling cheaper.

### Keyboard, focus and selection

The results were identical in all three browsers.

| Behaviour | FluentDataGrid | QuickGrid (plus the spike's `users-grid.js`) |
| -- | -- | -- |
| Tab from search into the grid | 5 header sort buttons, then a body cell | 5 header sort links, then the row's open button |
| Arrow keys between rows | Built in. Cell focus moves after a round trip to .NET; the harness waits 400 ms. | Added by the spike, immediate |
| Space on a row | Paged: no toggle, focus kept. Virtualized: focus lost to `body`, no toggle. | Toggles the row checkbox, focus kept (added by the spike) |
| Enter on a row | Opens the pane. On the select cell, Enter also toggles the selection. | Opens the pane |
| Escape closes the pane and returns focus to the row | Paged: yes. Virtualized: not reached, because focus was already lost. | Yes, in both modes |
| Clicking the select cell | Toggles selection and also fires `OnRowClick`. The spike suppresses row clicks within 250 ms of a toggle. | Plain checkbox |
| Deep link `?userId=` to row 30 (inside the first fetched server page) | Pane opens; `ScrollToItemAsync(30)` renders no row | Pane opens; row scrolled into view and selected |
| Deep link to row 400 (not fetched) | Pane opens with the "not in the current view" notice | Same |
| Focus inside the role dialog | The `fluent-dialog` element, not the first radio | Same |
| Focus after the role dialog closes | The row's action button | Same |

### Mutations against the API

Each was checked against a fresh API query after the grid updated. The results were identical in all three browsers.

- **Change role (Member to Admin) through the row menu and dialog:** the grid row and the API both show Admin.
- **Delete one user through the row menu and the confirmation dialog:** grid total 0 and API total 0 for that search.
- **Bulk delete three selected users from the toolbar:** grid and API both went from N to N − 3.
- **Filter dialog (role Admin, modified from 2020-01-01 to 2099-12-31):**
  - the query and URL carry all three filters;
  - the badge shows 2;
  - every rendered row is Admin;
  - the grid total equals the API total.
  - "Clear filters" returns to the unfiltered query.

## Defects and constraints found

1. **FluentDataGrid hangs when its parent re-renders during an ItemsProvider call.**
   - **Symptom:** the grid cancels the call, never asks for the range again, and stays empty.
   - **Where observed:** Chromium, with an `EventCallback` from the provider and with a render deferred by `Task.Yield`.
   - **Workaround:** notifications are plain delegates, and the surface renders only after the provider has returned
     (`UsersGridBase`).
   - **QuickGrid:** the same re-render was harmless.
2. **FluentMenu items render visibly unless they are inside `FluentMenuList`.** Without it, the component's script makes
   only the first item a popover.
3. **QuickGrid's .NET 11 URL state goes through NavigationManager.**
   - **Mechanism:** its sort headers and paginator are links writing `?sort=`, `?direction=` and `?page=`. In a
     WebAssembly island on a statically routed page, each click is an enhanced navigation that fetches the page from the
     server.
   - **The conflict:** NavigationManager does not see `history.replaceState`. A filter change therefore went through
     `NavigateTo` once QuickGrid's parameters were present; otherwise the recreated grid restored the old page.
   - **The rule to write:** the list's URL state and QuickGrid's parameters have to be one model.
4. **QuickGrid's Paginator text ("Page N of M") is English and has no template for it;** only the summary is
   templatable.
5. **FluentDataGrid's `ScrollToItemAsync` did not bring row 30 into view** when called after the first data load. The
   same call works on QuickGrid.
6. **A Blazor `@onkeydown` on the surface re-renders it on every key press.** Enter and Escape are handled in
   `users-grid.js` for this reason.
7. **QuickGrid has no selection column, no row keyboard model and no menu.** The spike added them in
   `QuickUsersGrid.razor` (116 lines, including the columns) and `users-grid.js` (74 lines, shared with the surface).
8. **Most seeded users have no name** (see "Tenant size").

## Verdict

**FluentDataGrid 5.0.0-preview.26254.1** can express the users list. Server sort and filter, virtualization, paging,
selection, row actions and the side pane all compose with it. It does not cover the product's list surfaces as the list
foundation:

- **Under the nonce-only policy:** it produces hundreds of `style-src-attr` violations per render, and its layout
  degrades.
- **With style attributes allowed:** it scrolls at roughly twice QuickGrid's cost per step in Chromium and WebKit, and
  over five times in Firefox. That is with 100 range requests where QuickGrid makes 2.
- **Keyboard and deep links:** it loses focus on Space when virtualized, does not toggle selection on Space, and did not
  scroll to a deep-linked row.
- **Provider calls:** it hangs when a parent renders during a provider call.

**QuickGrid 11.0.0-rc.1.26425.128** is the recommended list foundation:

- **Policy:** it runs under the unchanged nonce-only policy with 0 violations in all three browsers.
- **Performance:** it scrolls 10,001 rows at about 52 ms per three-viewport step, with p95 frame gaps of 22 to 44 ms.
- **Mutations and filters:** every one tested passed.

It needs a shared wrapper written once: selection model, row keyboard model, list URL state reconciled with its own
parameters, a localized paginator, and the server page cache. Those are M items in the capability map. FluentUI stays
the source of the surrounding controls: menu, dialog, select, text input and badge.

**Virtualization is usable under the proposed policy on .NET 11**, shown by QuickGrid. The acceptance branch "if
virtualization is unusable, recommend paging or a policy change" therefore does not trigger for lists.

**The policy change is recorded for B4 anyway**, because FluentUI components other than the grid also write style
attributes: FluentOverflow in B1, and the FluentDataGrid header markup here. The options:

- **Keep the nonce-only policy (recommended).**
  - *Security cost:* none.
  - *Product cost:* avoid FluentUI components whose markup carries style attributes, or wrap them. Today that means
    FluentDataGrid and FluentOverflow.
- **Add `style-src-attr 'unsafe-inline'` and keep the nonce on `style-src-elem`.**
  - *What it permits:* markup that reaches the page through an HTML injection flaw may carry style attributes. That
    enables overlay and redress attacks, hiding or spoofing of content, and `url()` requests to hosts the policy
    already allows. `<style>` elements and stylesheets from other origins stay blocked.
  - *What it does not permit:* selector-based attribute exfiltration, which needs a stylesheet.
  - *Reachability:* reaching it still needs an injection past Blazor's output encoding. It does not need the nonce
    defeated.
  - *What it buys:* it removed the violations and restored FluentDataGrid's layout. It did not fix FluentDataGrid's
    scroll cost, so it is not the price of a usable grid.
- **Paging instead of virtualization:** not needed for performance. QuickGrid paged and virtualized both work. Paging
  stays the desktop model because the React edition uses it (25 per page).

## Not done or not verified

- Real Safari. A trimmed Release publish of the users pages, and their payload with QuickGrid added.
- Touch and long-press row menus. Narrow viewports: the side pane full-screen mode, filters switching inline or dialog.
- Screen reader output.
- The recycle bin, invite dialog and tenant-name-required dialog. The capability map covers them from the React
  inventory only.
- Repeated samples for medians; every timing above is a single run.
- Server count-query cost beyond 10,001 users.
- Localization of the list strings (English only in the spike).
