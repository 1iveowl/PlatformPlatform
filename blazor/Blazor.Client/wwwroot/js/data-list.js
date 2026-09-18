// Browser work for DataList: the row keyboard model, row clicks with their modifier keys, the roving tab stop and the
// header checkbox's indeterminate state. Loaded as a module from the same origin through the import map, so it runs under
// the policy without a nonce; listeners are attached with addEventListener and removed on dispose. Nothing here writes a
// style attribute.
//
// Keys are handled here instead of with a Blazor keydown handler, which would render the list on every key press.
// Each list attaches its own listeners to its own root, so several lists on one page keep independent focus and selection.
// Keys and clicks that start in an interactive descendant (a text input, a checkbox, a button, a link, a menu) are left to
// that element.
//
// Touch: a press of LongPressMilliseconds on a row without moving more than LongPressTolerance pixels opens the row's menu
// through its menu button, and so does a right-click; the click that ends a long-press does not activate the row. A list
// that loads differently on phones gets the small breakpoint (40rem, the value Breakpoints.Small holds) reported on
// attach and on every crossing, and an IntersectionObserver asks for the next page when the sentinel after the last row
// scrolls into view; the list ignores a request while one is in flight.

const interactiveDescendant =
  "input, textarea, select, button, a[href], label, summary, [contenteditable]:not([contenteditable='false']), [role='menu'], [role='menuitem'], [role='menubar'], [role='listbox'], [role='option'], [data-list-interactive]";

const LongPressMilliseconds = 500;
const LongPressTolerance = 10;
const SmallBreakpointQuery = "(min-width: 40rem)";

function isTextEntry(element) {
  return element instanceof Element && element.closest("input, textarea, select, [contenteditable]:not([contenteditable='false'])") !== null;
}

export function attach(root, dotNet, options) {
  const multiple = options.selectionMode === "Multiple";
  const state = { activeIndex: -1, hasActivated: false };

  const rows = () => [...root.querySelectorAll("tbody tr.data-list-row")].filter((row) => row.closest(".data-list") === root);

  const rowOf = (element) => {
    const row = element instanceof Element ? element.closest("tr.data-list-row") : null;
    return row !== null && row.closest(".data-list") === root ? row : null;
  };

  const setTabStop = (target) => {
    for (const row of rows()) row.tabIndex = row === target ? 0 : -1;
  };

  const focusAt = (index) => {
    const all = rows();
    if (all.length === 0) return -1;
    const clamped = Math.max(0, Math.min(all.length - 1, index));
    setTabStop(all[clamped]);
    all[clamped].focus();
    return clamped;
  };

  // When the activated row is not on the loaded page, focus goes to a stable control instead: the element the page names,
  // else the first sort button, else the list itself
  const focusFallback = () => {
    const named = options.focusFallbackId ? document.getElementById(options.focusFallbackId) : null;
    const target = named ?? root.querySelector("button.data-list-sort") ?? root;
    if (target === root && !root.hasAttribute("tabindex")) root.tabIndex = -1;
    target.focus();
  };

  const onKeyDown = (event) => {
    if (event.defaultPrevented || event.altKey || event.key === "Escape") return;
    const row = event.target instanceof Element && event.target.matches("tr.data-list-row") ? rowOf(event.target) : null;
    if (row === null) return;
    const all = rows();
    const index = all.indexOf(row);
    const move = (target) => {
      const reached = focusAt(target);
      if (event.shiftKey && multiple && reached !== index) dotNet.invokeMethodAsync("ExtendSelection", index, reached);
    };

    switch (event.key) {
      case "ArrowDown":
        move(index + 1);
        break;
      case "ArrowUp":
        move(index - 1);
        break;
      case "Home":
        move(0);
        break;
      case "End":
        move(all.length - 1);
        break;
      case " ":
        if (!multiple) return;
        dotNet.invokeMethodAsync("ToggleRow", index);
        break;
      case "Enter":
        dotNet.invokeMethodAsync("ActivateRow", index);
        break;
      default:
        return;
    }
    event.preventDefault();
  };

  // Escape can come from the row or from the element the page names as the list's detail, such as a side pane
  const onDocumentKeyDown = (event) => {
    if (event.key !== "Escape" || event.defaultPrevented || !state.hasActivated) return;
    const target = event.target instanceof Element ? event.target : null;
    if (target === null || isTextEntry(target) || target.closest("dialog[open], [role='menu']") !== null) return;
    const scope = options.escapeScopeId ? document.getElementById(options.escapeScopeId) : null;
    if (!root.contains(target) && (scope === null || !scope.contains(target))) return;
    event.preventDefault();
    const returnIndex = state.activeIndex;
    dotNet.invokeMethodAsync("CloseFromKeyboard").then(() => {
      if (returnIndex < 0 || focusAt(returnIndex) < 0) focusFallback();
    });
  };

  // Shift+click would otherwise select the text between the anchor and the row
  const onMouseDown = (event) => {
    if (event.shiftKey && multiple && rowOf(event.target) !== null && event.target.closest(interactiveDescendant) === null) event.preventDefault();
  };

  const onClick = (event) => {
    if (event.defaultPrevented || event.button !== 0) return;
    const row = rowOf(event.target);
    if (row === null || event.target.closest(interactiveDescendant) !== null) return;
    setTabStop(row);
    row.focus();
    dotNet.invokeMethodAsync("ClickRow", rows().indexOf(row), event.ctrlKey || event.metaKey, event.shiftKey);
  };

  const openRowMenu = (row) => {
    const trigger = row.querySelector(".data-list-row-menu-trigger");
    if (trigger === null || trigger.disabled || trigger.getAttribute("aria-expanded") === "true") return false;
    trigger.click();
    return true;
  };

  const press = { timer: 0, pointerId: -1, x: 0, y: 0, openedAt: -Infinity };

  const cancelPress = () => {
    window.clearTimeout(press.timer);
    press.timer = 0;
  };

  const onPointerDown = (event) => {
    cancelPress();
    // A new press starts a new gesture, so a click after it is never the one that ended a long-press
    press.openedAt = -Infinity;
    if (event.pointerType === "mouse" || !event.isPrimary) return;
    const row = rowOf(event.target);
    if (row === null || event.target.closest(interactiveDescendant) !== null) return;
    press.pointerId = event.pointerId;
    press.x = event.clientX;
    press.y = event.clientY;
    press.timer = window.setTimeout(() => {
      press.timer = 0;
      if (openRowMenu(row)) press.openedAt = performance.now();
    }, LongPressMilliseconds);
  };

  // Only the pressing pointer ends or moves a press; a mouse moving on a touch device leaves it running
  const isPressingPointer = (event) => press.timer !== 0 && event.pointerId === press.pointerId;

  const onPointerMove = (event) => {
    if (isPressingPointer(event) && Math.hypot(event.clientX - press.x, event.clientY - press.y) > LongPressTolerance) cancelPress();
  };

  const onPointerEnd = (event) => {
    if (isPressingPointer(event)) cancelPress();
  };

  const recentlyPressed = () => performance.now() - press.openedAt < 1000;

  // A touch long-press also raises contextmenu in some browsers; the menu is already open then
  const onContextMenu = (event) => {
    const row = rowOf(event.target);
    if (row === null || event.target.closest(interactiveDescendant) !== null) return;
    event.preventDefault();
    if (!recentlyPressed()) openRowMenu(row);
  };

  // The click that ends a long-press lands on the row or on the backdrop of the menu it opened, and must neither activate
  // the row nor close the menu
  const onClickCapture = (event) => {
    if (!recentlyPressed()) return;
    press.openedAt = -Infinity;
    event.preventDefault();
    event.stopPropagation();
  };

  const smallQuery = options.reportsBreakpoint ? window.matchMedia(SmallBreakpointQuery) : null;
  const onBreakpointChange = () => dotNet.invokeMethodAsync("SetCompact", !smallQuery.matches);

  let observedSentinel = null;
  const observer =
    typeof IntersectionObserver === "function"
      ? new IntersectionObserver((entries) => {
          if (entries.some((entry) => entry.isIntersecting)) dotNet.invokeMethodAsync("LoadNext");
        })
      : null;

  const onFocusIn = (event) => {
    const row = rowOf(event.target);
    if (row !== null && event.target === row) setTabStop(row);
  };

  root.addEventListener("keydown", onKeyDown);
  root.addEventListener("mousedown", onMouseDown);
  root.addEventListener("click", onClick);
  root.addEventListener("focusin", onFocusIn);
  root.addEventListener("pointerdown", onPointerDown);
  root.addEventListener("pointermove", onPointerMove);
  root.addEventListener("pointerup", onPointerEnd);
  root.addEventListener("pointercancel", onPointerEnd);
  root.addEventListener("contextmenu", onContextMenu);
  root.addEventListener("click", onClickCapture, true);
  document.addEventListener("keydown", onDocumentKeyDown);
  if (smallQuery !== null) {
    smallQuery.addEventListener("change", onBreakpointChange);
    if (!smallQuery.matches) onBreakpointChange();
  }

  return {
    // Called after every render: rows the grid rendered again have lost their tab index
    sync: (activeIndex, headerSelection, hasActivated, canLoadMore) => {
      state.activeIndex = activeIndex;
      state.hasActivated = hasActivated === true;
      const all = rows();
      const focused = all.find((row) => row === document.activeElement);
      const tabStop = focused ?? all[activeIndex] ?? all.find((row) => row.tabIndex === 0) ?? all[0];
      if (tabStop !== undefined) setTabStop(tabStop);
      const selectAll = root.querySelector("input[data-list-select-all]");
      if (selectAll !== null) selectAll.indeterminate = headerSelection === "Some";
      // Observing again reports the sentinel's current intersection, so a page too short to scroll keeps loading
      if (observer !== null) {
        if (observedSentinel !== null) observer.unobserve(observedSentinel);
        observedSentinel = canLoadMore === true ? root.querySelector("[data-list-sentinel]") : null;
        if (observedSentinel !== null) observer.observe(observedSentinel);
      }
    },
    // After the load mode changed, the activated row is scrolled back into view
    revealActive: (index) => {
      const row = rows()[index];
      if (row !== undefined) row.scrollIntoView({ block: "nearest" });
    },
    focusRow: (index) => focusAt(index) >= 0,
    focusFallback,
    dispose: () => {
      root.removeEventListener("keydown", onKeyDown);
      root.removeEventListener("mousedown", onMouseDown);
      root.removeEventListener("click", onClick);
      root.removeEventListener("focusin", onFocusIn);
      root.removeEventListener("pointerdown", onPointerDown);
      root.removeEventListener("pointermove", onPointerMove);
      root.removeEventListener("pointerup", onPointerEnd);
      root.removeEventListener("pointercancel", onPointerEnd);
      root.removeEventListener("contextmenu", onContextMenu);
      root.removeEventListener("click", onClickCapture, true);
      document.removeEventListener("keydown", onDocumentKeyDown);
      smallQuery?.removeEventListener("change", onBreakpointChange);
      observer?.disconnect();
      cancelPress();
    }
  };
}
