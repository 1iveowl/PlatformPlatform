// Browser work for the authenticated shell (AppShell and InstallPrompt). Loaded as a same-origin module through the import
// map, so it runs under the policy's script-src-elem host list without a nonce. Listeners are attached with
// addEventListener and removed by dispose, so a component disposed by enhanced navigation leaves no hotkey or media
// listener behind. Nothing here writes a style attribute: every state is a class or a data attribute set by .NET.
// Storage can be denied (private modes, blocked site data); every read and write falls back instead of throwing, and the
// decisions about the values read live in ShellSidebarState and InstallPromptPolicy.

// The literals ShellBreakpoints holds; ShellBreakpointsTests fails when they drift from it or from app.css
const extraLargeQuery = "(min-width: 80rem)";
const smallQuery = "(min-width: 40rem)";

const sidebarCollapsedKey = "side-menu-collapsed";
const installPromptDismissedKey = "add-to-homescreen-dismissed";
const installPromptDismissedForSessionKey = "add-to-homescreen-dismissed_session";
const swipeDismissDistancePixels = 50;

const ON_TOGGLE_SIDEBAR = "ToggleSidebar";
const ON_VIEWPORT_CHANGED = "OnViewportChanged";
const ON_INSTALL_PROMPT_SWIPED = "OnInstallPromptSwiped";

function readStorage(storage, key) {
  try {
    return storage().getItem(key);
  } catch {
    return null;
  }
}

function writeStorage(storage, key, value) {
  try {
    storage().setItem(key, value);
  } catch {
    // Storage denied: the choice lasts for this document only
  }
}

async function invoke(dotNet, method, ...args) {
  try {
    await dotNet.invokeMethodAsync(method, ...args);
  } catch {
    // The component is gone or the runtime is leaving the document
  }
}

function viewport() {
  return { isWide: window.matchMedia(extraLargeQuery).matches, isSmall: !window.matchMedia(smallQuery).matches };
}

export function attachShell(dotNet) {
  const abort = new AbortController();
  const extraLarge = window.matchMedia(extraLargeQuery);
  const small = window.matchMedia(smallQuery);

  document.addEventListener(
    "keydown",
    (event) => {
      if (event.key.toLowerCase() !== "b" || !(event.ctrlKey || event.metaKey) || event.altKey || event.shiftKey || event.repeat) return;
      event.preventDefault();
      invoke(dotNet, ON_TOGGLE_SIDEBAR);
    },
    { signal: abort.signal }
  );
  const onViewportChanged = () => invoke(dotNet, ON_VIEWPORT_CHANGED, viewport());
  extraLarge.addEventListener("change", onViewportChanged, { signal: abort.signal });
  small.addEventListener("change", onViewportChanged, { signal: abort.signal });

  return {
    readState: () => ({ ...viewport(), storedCollapsed: readStorage(() => localStorage, sidebarCollapsedKey) }),
    storeCollapsed: (value) => writeStorage(() => localStorage, sidebarCollapsedKey, value),
    dispose: () => abort.abort()
  };
}

export function readInstallPromptEnvironment() {
  let isStandalone = false;
  try {
    isStandalone = window.matchMedia("(display-mode: standalone)").matches || navigator.standalone === true;
  } catch {
    isStandalone = false;
  }
  return {
    userAgent: navigator.userAgent,
    maxTouchPoints: navigator.maxTouchPoints ?? 0,
    isStandalone,
    dismissedUntil: readStorage(() => localStorage, installPromptDismissedKey),
    dismissedForSession: readStorage(() => sessionStorage, installPromptDismissedForSessionKey) === "true"
  };
}

export function storeInstallPromptDismissal(dismissedUntil) {
  if (dismissedUntil === null) writeStorage(() => sessionStorage, installPromptDismissedForSessionKey, "true");
  else writeStorage(() => localStorage, installPromptDismissedKey, dismissedUntil);
}

// An upward swipe on the banner dismisses it for the session, as in React; shorter swipes do nothing
export function attachInstallPromptSwipe(element, dotNet) {
  const abort = new AbortController();
  let startY = null;
  element.addEventListener("touchstart", (event) => (startY = event.touches[0]?.clientY ?? null), { passive: true, signal: abort.signal });
  element.addEventListener(
    "touchend",
    (event) => {
      const endY = event.changedTouches[0]?.clientY;
      if (startY !== null && endY !== undefined && startY - endY > swipeDismissDistancePixels) invoke(dotNet, ON_INSTALL_PROMPT_SWIPED);
      startY = null;
    },
    { passive: true, signal: abort.signal }
  );
  return { dispose: () => abort.abort() };
}

// Moves focus to an element of a menu or the control that opened it; a missing element (already re-rendered away) is ignored
export function focusElement(id) {
  document.getElementById(id)?.focus();
}

// The host's js/theme.js owns the theme decision and storage on every page; these dispatch its document events
// synchronously and return the detail it filled, or null when the script is not on the document
export function setTheme(theme) {
  const detail = { theme };
  document.dispatchEvent(new CustomEvent("theme:set", { detail }));
  return detail.resolvedTheme === undefined ? null : detail;
}

export function readTheme() {
  const detail = {};
  document.dispatchEvent(new CustomEvent("theme:read", { detail }));
  return detail.theme === undefined ? null : detail;
}
