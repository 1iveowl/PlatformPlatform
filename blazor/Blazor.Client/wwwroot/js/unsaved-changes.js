// Browser work for UnsavedChangesGuard and the modal dialogs. Loaded as a module from the same origin, so it runs under the
// policy's script-src-elem host list without a nonce. Listeners are attached with addEventListener, and nothing here writes
// a style attribute: dialogs use the native <dialog> element, styled from the external stylesheet.
//
// Every page is a server-rendered page on one router with plain links, so the framework's location-changing handlers only
// see navigations started from .NET. Without an interactive router, enhanced navigation handles link clicks and
// Back/Forward in blazor.web.js without asking them. This module therefore guards what the framework cannot:
// - in-app links: a capture-phase click listener on the document runs before the framework's bubbling one
// - Back and Forward inside the document: a capture-phase popstate listener on window runs before the framework's
//   listener, returns to the guarded history entry and asks .NET; the Navigation API supplies the entry index
// - document unload (reload, address bar, external links, traversal to another document): beforeunload, whose prompt
//   wording belongs to the browser

function currentEntryIndex() {
  return typeof navigation === "undefined" || navigation.currentEntry === null ? null : navigation.currentEntry.index;
}

function isGuardedClick(event) {
  if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return null;
  const anchor = event.target instanceof Element ? event.target.closest("a[href]") : null;
  if (anchor === null || anchor.hasAttribute("download")) return null;
  if (anchor.target !== "" && anchor.target !== "_self") return null;
  const target = new URL(anchor.href, location.href);
  // Another origin unloads the document, which beforeunload guards
  if (target.origin !== location.origin) return null;
  // A fragment on the current page is not a navigation away from the edits
  if (target.hash !== "" && target.pathname === location.pathname && target.search === location.search) return null;
  return anchor;
}

// Follows a blocked link the way the framework would have: enhanced unless an ancestor opts out with data-enhance-nav,
// through the framework's own navigation; a synthetic click is not treated alike by every browser
function followLink(anchor) {
  const optOut = anchor.closest("[data-enhance-nav]")?.getAttribute("data-enhance-nav");
  const isEnhanced = optOut === undefined || optOut === "" || optOut.toLowerCase() === "true";
  if (isEnhanced && typeof window.Blazor?.navigateTo === "function") window.Blazor.navigateTo(anchor.href);
  else location.assign(anchor.href);
}

export function attachNavigationGuard(dotNet) {
  let isDirty = false;
  let guardedEntryIndex = currentEntryIndex();
  let pending = null;
  let isRestoring = false;

  const onClick = (event) => {
    if (!isDirty) return;
    const anchor = isGuardedClick(event);
    if (anchor === null) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    pending = { kind: "link", anchor };
    dotNet.invokeMethodAsync("OnNavigationBlocked");
  };

  const onPopState = (event) => {
    if (isRestoring) {
      isRestoring = false;
      event.stopImmediatePropagation();
      return;
    }
    const index = currentEntryIndex();
    if (!isDirty || index === null || guardedEntryIndex === null || index === guardedEntryIndex) return;
    event.stopImmediatePropagation();
    const delta = index - guardedEntryIndex;
    isRestoring = true;
    pending = { kind: "traverse", delta };
    history.go(-delta);
    dotNet.invokeMethodAsync("OnNavigationBlocked");
  };

  const onBeforeUnload = (event) => {
    if (!isDirty) return;
    event.preventDefault();
    // Still required by some browsers to show the prompt
    event.returnValue = "";
  };

  document.addEventListener("click", onClick, { capture: true });
  window.addEventListener("popstate", onPopState, { capture: true });
  window.addEventListener("beforeunload", onBeforeUnload);

  return {
    setDirty: (value) => {
      isDirty = value;
      if (value) guardedEntryIndex = currentEntryIndex();
      else pending = null;
    },
    // Leave: the edits are discarded and the blocked navigation runs once, now unguarded
    proceed: () => {
      const blocked = pending;
      pending = null;
      isDirty = false;
      if (blocked?.kind === "link") followLink(blocked.anchor);
      else if (blocked?.kind === "traverse") history.go(blocked.delta);
    },
    // Stay: the document and the edits remain
    cancel: () => {
      pending = null;
    },
    dispose: () => {
      document.removeEventListener("click", onClick, { capture: true });
      window.removeEventListener("popstate", onPopState, { capture: true });
      window.removeEventListener("beforeunload", onBeforeUnload);
    }
  };
}

// A native modal dialog: showModal gives the top layer, the backdrop, inert content behind it and focus containment.
// Escape raises cancel and a click on the backdrop targets the dialog element itself; both ask .NET instead of closing.
export function attachModalDialog(dialog, dotNet, closesOnBackdrop) {
  const onCancel = (event) => {
    event.preventDefault();
    dotNet.invokeMethodAsync("RequestDismiss");
  };
  const onClick = (event) => {
    if (closesOnBackdrop && event.target === dialog) dotNet.invokeMethodAsync("RequestDismiss");
  };
  dialog.addEventListener("cancel", onCancel);
  dialog.addEventListener("click", onClick);

  return {
    show: () => {
      if (!dialog.open) dialog.showModal();
    },
    close: () => {
      if (dialog.open) dialog.close();
    },
    dispose: () => {
      dialog.removeEventListener("cancel", onCancel);
      dialog.removeEventListener("click", onClick);
      if (dialog.open) dialog.close();
    }
  };
}
