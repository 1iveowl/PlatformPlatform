// Applies the theme mode and the zoom level before first paint on every page, public and authenticated. The host page loads
// it with the nonce as a parser-blocking script in <head>, after the theme-color meta element and the stylesheets, because a
// module is deferred and could run after the first paint; nothing here is inline and nothing writes a style attribute.
//
// The mode is stored under "theme" as system, light or dark (a missing or unknown value is system, and system follows
// prefers-color-scheme), the same decision as ThemePreference in Blazor.Client. The result is data-theme (light or dark)
// and data-theme-mode on <html>, which select the palette in app.css and brand.css, plus the theme-color meta content.
//
// The zoom level is stored under "zoom-level" as 0.875, 1.125 or 1.25; the default 1 is stored as no value, and a missing
// or unknown value is the default, the same decision as ZoomPreference in Blazor.Client. The result is data-zoom-level on
// <html> for a level other than the default, which app.css turns into the --zoom-level variable.
//
// It also drives the static theme and language menus (ThemeMenu, LanguageMenu) through listeners on the document, so the
// menus work on public pages, which have no runtime, and survive enhanced navigation. A language chosen there is written to
// the preferred-locale cookie, which the host reads after a signed-in user's locale claim, and the page is loaded again in
// it. The WebAssembly client changes and reads the theme and the zoom level and remembers a saved language through the
// "theme:set", "theme:read", "zoom:set", "zoom:read" and "locale:remember" document events, which shell.js dispatches
// synchronously with a detail object this script fills, so the decisions, the storage and the cookie stay in one place.
(() => {
  const storageKey = "theme";
  const modes = ["system", "light", "dark"];
  const zoomStorageKey = "zoom-level";
  const zoomLevels = ["0.875", "1", "1.125", "1.25"];
  const defaultZoomLevel = "1";
  const localeCookieName = "preferred-locale";
  const locales = ["en-US", "da-DK"];
  const localeCookieMaxAgeSeconds = 31536000;
  const root = document.documentElement;
  const darkQuery = window.matchMedia("(prefers-color-scheme: dark)");

  function readStored(key) {
    try {
      return localStorage.getItem(key);
    } catch {
      return null;
    }
  }

  function store(key, value) {
    try {
      if (value === null) localStorage.removeItem(key);
      else localStorage.setItem(key, value);
    } catch {
      // Storage is denied; the value applies to this document only
    }
  }

  function parse(value) {
    return modes.includes(value) ? value : "system";
  }

  function parseZoomLevel(value) {
    return zoomLevels.includes(value) ? value : defaultZoomLevel;
  }

  function resolve(mode) {
    if (mode !== "system") return mode;
    return darkQuery.matches ? "dark" : "light";
  }

  let current = parse(readStored(storageKey));
  let currentZoomLevel = parseZoomLevel(readStored(zoomStorageKey));

  function syncChoices() {
    for (const choice of document.querySelectorAll("[data-theme-menu] [data-theme-choice]")) {
      const checked = String(choice.dataset.themeChoice === current);
      if (choice.getAttribute("aria-checked") !== checked) choice.setAttribute("aria-checked", checked);
    }
  }

  function applyZoomLevel() {
    if (currentZoomLevel === defaultZoomLevel) {
      if (root.hasAttribute("data-zoom-level")) root.removeAttribute("data-zoom-level");
    } else if (root.dataset.zoomLevel !== currentZoomLevel) {
      root.dataset.zoomLevel = currentZoomLevel;
    }
  }

  function apply() {
    const resolved = resolve(current);
    if (root.dataset.theme !== resolved) root.dataset.theme = resolved;
    if (root.dataset.themeMode !== current) root.dataset.themeMode = current;
    const meta = document.querySelector('meta[name="theme-color"]');
    const color = resolved === "dark" ? meta?.dataset.dark : meta?.dataset.light;
    if (meta && color && meta.content !== color) meta.content = color;
    applyZoomLevel();
    syncChoices();
    return resolved;
  }

  function select(mode) {
    const from = current;
    current = parse(mode);
    store(storageKey, current);
    return { fromTheme: from, theme: current, resolvedTheme: apply() };
  }

  function selectZoomLevel(level) {
    const from = currentZoomLevel;
    currentZoomLevel = parseZoomLevel(level);
    store(zoomStorageKey, currentZoomLevel === defaultZoomLevel ? null : currentZoomLevel);
    apply();
    return { fromZoomLevel: from, zoomLevel: currentZoomLevel };
  }

  function rememberLocale(locale) {
    if (!locales.includes(locale)) return false;
    document.cookie = `${localeCookieName}=${locale}; Path=/; Max-Age=${localeCookieMaxAgeSeconds}; Secure; SameSite=Lax`;
    return true;
  }

  // The public language menu: remember the choice and load the page again, so the host renders it in that language
  function selectLocale(locale) {
    if (!rememberLocale(locale) || locale === root.lang) return;
    window.location.replace(window.location.href.split("#")[0]);
  }

  // The two static menus share the opening, closing and keyboard behaviour and differ in their attributes and in what a
  // choice does
  const menuKinds = [
    { name: "theme", current: () => current, choose: (value) => select(value) },
    { name: "language", current: () => root.lang, choose: (value) => selectLocale(value) }
  ].map((kind) => ({
    ...kind,
    menu: `[data-${kind.name}-menu]`,
    trigger: `[data-${kind.name}-menu-trigger]`,
    list: `[data-${kind.name}-menu-list]`,
    choice: `[data-${kind.name}-choice]`,
    choiceValue: (element) => element.getAttribute(`data-${kind.name}-choice`)
  }));

  function kindOf(menu) {
    return menuKinds.find((kind) => menu.matches(kind.menu));
  }

  function closestMenu(target) {
    for (const kind of menuKinds) {
      const menu = target?.closest(kind.menu);
      if (menu) return menu;
    }
    return null;
  }

  function listOf(menu) {
    return menu.querySelector(kindOf(menu).list);
  }

  function triggerOf(menu) {
    return menu.querySelector(kindOf(menu).trigger);
  }

  function itemsOf(menu) {
    return [...listOf(menu).querySelectorAll(kindOf(menu).choice)];
  }

  function openMenu(menu) {
    closeMenus(null);
    listOf(menu).hidden = false;
    triggerOf(menu).setAttribute("aria-expanded", "true");
    const kind = kindOf(menu);
    const items = itemsOf(menu);
    (items.find((item) => kind.choiceValue(item) === kind.current()) ?? items[0])?.focus();
  }

  function closeMenu(menu, returnFocus) {
    const list = listOf(menu);
    if (list.hidden) return;
    list.hidden = true;
    triggerOf(menu).setAttribute("aria-expanded", "false");
    if (returnFocus) triggerOf(menu).focus();
  }

  function closeMenus(keep) {
    for (const kind of menuKinds) {
      for (const menu of document.querySelectorAll(kind.menu)) {
        if (menu !== keep) closeMenu(menu, false);
      }
    }
  }

  document.addEventListener("click", (event) => {
    const target = event.target instanceof Element ? event.target : null;
    const menu = closestMenu(target);
    closeMenus(menu);
    if (menu === null) return;

    const kind = kindOf(menu);
    if (target.closest(kind.trigger)) {
      if (listOf(menu).hidden) openMenu(menu);
      else closeMenu(menu, true);
      return;
    }

    const choice = target.closest(kind.choice);
    if (choice === null) return;
    closeMenu(menu, true);
    kind.choose(kind.choiceValue(choice));
  });

  document.addEventListener("keydown", (event) => {
    const target = event.target instanceof Element ? event.target : null;
    const menu = closestMenu(target);
    if (!menu) return;

    const kind = kindOf(menu);
    if (target.closest(kind.trigger)) {
      if (event.key === "ArrowDown" && listOf(menu).hidden) {
        event.preventDefault();
        openMenu(menu);
      }
      return;
    }

    const items = itemsOf(menu);
    const index = items.indexOf(target.closest(kind.choice));
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
  document.addEventListener("zoom:set", (event) => Object.assign(event.detail, selectZoomLevel(event.detail.zoomLevel)));
  document.addEventListener("zoom:read", (event) => Object.assign(event.detail, { zoomLevel: currentZoomLevel }));
  document.addEventListener("locale:remember", (event) => Object.assign(event.detail, { remembered: rememberLocale(event.detail.locale) }));

  darkQuery.addEventListener("change", () => apply());

  // Another tab of the same browser changed or cleared a stored preference
  window.addEventListener("storage", (event) => {
    if (event.key !== storageKey && event.key !== zoomStorageKey && event.key !== null) return;
    current = parse(readStored(storageKey));
    currentZoomLevel = parseZoomLevel(readStored(zoomStorageKey));
    apply();
  });

  // Enhanced navigation synchronizes the document element's attributes and the head with the new document, which carries
  // none of these attributes; restoring them from a mutation callback runs before the next paint
  new MutationObserver(() => apply()).observe(root, { attributes: true, attributeFilter: ["data-theme", "data-theme-mode", "data-zoom-level"] });

  document.addEventListener("DOMContentLoaded", () => {
    apply();
    window.Blazor?.addEventListener("enhancedload", () => {
      closeMenus(null);
      apply();
    });
  });

  apply();
})();
