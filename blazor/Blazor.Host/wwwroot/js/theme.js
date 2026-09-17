// Applies the theme mode before first paint on every page, public and authenticated. The host page loads it with the nonce
// as a parser-blocking script in <head>, after the theme-color meta element and the stylesheets, because a module is
// deferred and could run after the first paint; nothing here is inline and nothing writes a style attribute. The mode is
// stored under "theme" as system, light or dark (a missing or unknown value is system, and system follows
// prefers-color-scheme), the same decision as ThemePreference in Blazor.Client. The result is data-theme (light or dark)
// and data-theme-mode on <html>, which select the palette in app.css and brand.css, plus the theme-color meta content.
//
// It also drives the static theme menu (ThemeMenu) through listeners on the document, so the menu works on public pages,
// which have no runtime, and survives enhanced navigation. The WebAssembly shell changes and reads the theme through the
// "theme:set" and "theme:read" document events, which shell.js dispatches synchronously with a detail object this script
// fills, so the decision and storage stay in one place.
(() => {
  const storageKey = "theme";
  const modes = ["system", "light", "dark"];
  const root = document.documentElement;
  const darkQuery = window.matchMedia("(prefers-color-scheme: dark)");

  function readStored() {
    try {
      return localStorage.getItem(storageKey);
    } catch {
      return null;
    }
  }

  function store(mode) {
    try {
      localStorage.setItem(storageKey, mode);
    } catch {
      // Storage is denied; the mode applies to this document only
    }
  }

  function parse(value) {
    return modes.includes(value) ? value : "system";
  }

  function resolve(mode) {
    if (mode !== "system") return mode;
    return darkQuery.matches ? "dark" : "light";
  }

  let current = parse(readStored());

  function syncChoices() {
    for (const choice of document.querySelectorAll("[data-theme-menu] [data-theme-choice]")) {
      const checked = String(choice.dataset.themeChoice === current);
      if (choice.getAttribute("aria-checked") !== checked) choice.setAttribute("aria-checked", checked);
    }
  }

  function apply() {
    const resolved = resolve(current);
    if (root.dataset.theme !== resolved) root.dataset.theme = resolved;
    if (root.dataset.themeMode !== current) root.dataset.themeMode = current;
    const meta = document.querySelector('meta[name="theme-color"]');
    const color = resolved === "dark" ? meta?.dataset.dark : meta?.dataset.light;
    if (meta && color && meta.content !== color) meta.content = color;
    syncChoices();
    return resolved;
  }

  function select(mode) {
    const from = current;
    current = parse(mode);
    store(current);
    return { fromTheme: from, theme: current, resolvedTheme: apply() };
  }

  function listOf(menu) {
    return menu.querySelector("[data-theme-menu-list]");
  }

  function triggerOf(menu) {
    return menu.querySelector("[data-theme-menu-trigger]");
  }

  function itemsOf(menu) {
    return [...listOf(menu).querySelectorAll("[data-theme-choice]")];
  }

  function openMenu(menu) {
    closeMenus(null);
    listOf(menu).hidden = false;
    triggerOf(menu).setAttribute("aria-expanded", "true");
    const items = itemsOf(menu);
    (items.find((item) => item.dataset.themeChoice === current) ?? items[0])?.focus();
  }

  function closeMenu(menu, returnFocus) {
    const list = listOf(menu);
    if (list.hidden) return;
    list.hidden = true;
    triggerOf(menu).setAttribute("aria-expanded", "false");
    if (returnFocus) triggerOf(menu).focus();
  }

  function closeMenus(keep) {
    for (const menu of document.querySelectorAll("[data-theme-menu]")) {
      if (menu !== keep) closeMenu(menu, false);
    }
  }

  document.addEventListener("click", (event) => {
    const target = event.target instanceof Element ? event.target : null;
    const menu = target?.closest("[data-theme-menu]") ?? null;
    closeMenus(menu);
    if (menu === null) return;

    if (target.closest("[data-theme-menu-trigger]")) {
      if (listOf(menu).hidden) openMenu(menu);
      else closeMenu(menu, true);
      return;
    }

    const choice = target.closest("[data-theme-choice]");
    if (choice === null) return;
    select(choice.dataset.themeChoice);
    closeMenu(menu, true);
  });

  document.addEventListener("keydown", (event) => {
    const target = event.target instanceof Element ? event.target : null;
    const menu = target?.closest("[data-theme-menu]");
    if (!menu) return;

    if (target.closest("[data-theme-menu-trigger]")) {
      if (event.key === "ArrowDown" && listOf(menu).hidden) {
        event.preventDefault();
        openMenu(menu);
      }
      return;
    }

    const items = itemsOf(menu);
    const index = items.indexOf(target.closest("[data-theme-choice]"));
    if (index < 0) return;
    const next = { ArrowDown: (index + 1) % items.length, ArrowUp: (index - 1 + items.length) % items.length, Home: 0, End: items.length - 1 }[event.key];
    if (next !== undefined) {
      event.preventDefault();
      items[next].focus();
    } else if (event.key === "Escape") {
      event.preventDefault();
      closeMenu(menu, true);
    } else if (event.key === "Tab") {
      closeMenu(menu, false);
    }
  });

  document.addEventListener("theme:set", (event) => Object.assign(event.detail, select(event.detail.theme)));
  document.addEventListener("theme:read", (event) => Object.assign(event.detail, { theme: current, resolvedTheme: resolve(current) }));

  darkQuery.addEventListener("change", () => apply());

  // Another tab of the same browser changed or cleared the stored mode
  window.addEventListener("storage", (event) => {
    if (event.key !== storageKey && event.key !== null) return;
    current = parse(readStored());
    apply();
  });

  // Enhanced navigation synchronizes the document element's attributes and the head with the new document, which carries
  // neither attribute; restoring them from a mutation callback runs before the next paint
  new MutationObserver(() => apply()).observe(root, { attributes: true, attributeFilter: ["data-theme", "data-theme-mode"] });

  document.addEventListener("DOMContentLoaded", () => {
    apply();
    window.Blazor?.addEventListener("enhancedload", () => {
      closeMenus(null);
      apply();
    });
  });

  apply();
})();
