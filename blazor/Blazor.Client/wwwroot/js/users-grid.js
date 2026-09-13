// Spike code (Blazor edition, stage B3): browser work the users list needs that neither grid does. Loaded as a module from
// the same origin, so it runs under the policy's script-src-elem host list without a nonce, and attaches its listeners with
// addEventListener rather than inline handlers.

// URL state without a navigation: NavigationManager.NavigateTo from a WebAssembly island inside a statically routed page
// is an enhanced navigation that fetches the page from the server again
export function replaceUrl(url) {
  history.replaceState(history.state, "", url);
}

function rowOf(element) {
  return element?.closest("tr, [role='row']") ?? null;
}

function markerOf(row) {
  return row?.querySelector("[data-user-row]") ?? null;
}

export function focusedRowEmail() {
  return markerOf(rowOf(document.activeElement))?.dataset.email ?? null;
}

// Focus restoration: the row itself when it is focusable (FluentDataGrid cells carry tabindex), otherwise its open button
export function focusRow(root, email) {
  const marker = root.querySelector(`[data-user-row][data-email="${CSS.escape(email)}"]`);
  const row = rowOf(marker);
  if (!row) return false;
  const target = row.querySelector("[data-row-focus]") ?? row.querySelector("td[tabindex], [role='gridcell'][tabindex]") ?? row;
  target.focus();
  return document.activeElement === target;
}

// Enter on a focused row opens the profile and Escape closes it. Handled here rather than with @onkeydown, because a Blazor
// keydown handler re-renders the surface on every key press, including arrow keys the grid handles itself
export function attachSurfaceKeys(root, pane, dotNet) {
  const handler = (event) => {
    if (event.key === "Escape" && !pane.hidden) {
      event.preventDefault();
      dotNet.invokeMethodAsync("CloseProfileFromKeyboard");
      return;
    }
    if (event.key !== "Enter" || !root.contains(document.activeElement)) return;
    const email = focusedRowEmail();
    if (email) dotNet.invokeMethodAsync("OpenProfileFromKeyboard", email);
  };
  document.addEventListener("keydown", handler);
  return { dispose: () => document.removeEventListener("keydown", handler) };
}

// QuickGrid renders a plain table with no row keyboard model; this adds arrow keys, Home and End between the rows' open
// buttons and Space to toggle the row checkbox, which FluentDataGrid provides itself
export function attachRowKeyboard(root) {
  const handler = (event) => {
    const row = rowOf(document.activeElement);
    if (!row || !root.contains(row)) return;
    const rows = [...root.querySelectorAll("tbody tr")].filter((candidate) => markerOf(candidate));
    const position = rows.indexOf(row);
    const focusAt = (index) => rows[Math.max(0, Math.min(rows.length - 1, index))]?.querySelector("[data-row-focus]")?.focus();
    if (event.key === "ArrowDown") focusAt(position + 1);
    else if (event.key === "ArrowUp") focusAt(position - 1);
    else if (event.key === "Home" && event.ctrlKey) focusAt(0);
    else if (event.key === "End" && event.ctrlKey) focusAt(rows.length - 1);
    else if (event.key === " " && document.activeElement?.matches("[data-row-focus]")) row.querySelector("input[type='checkbox']")?.click();
    else return;
    event.preventDefault();
  };
  root.addEventListener("keydown", handler);
  return { dispose: () => root.removeEventListener("keydown", handler) };
}

export function scrollToTop(root) {
  const scroller = root.querySelector("[data-testid='grid-scroll']");
  if (scroller) scroller.scrollTop = 0;
}
