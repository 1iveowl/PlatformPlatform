// Browser work for DataList: the row keyboard model, row clicks with their modifier keys, the roving tab stop and the
// header checkbox's indeterminate state. Loaded as a module from the same origin through the import map, so it runs under
// the policy without a nonce; listeners are attached with addEventListener and removed on dispose. Nothing here writes a
// style attribute.
//
// Keys are handled here instead of with a Blazor keydown handler, which would render the list on every key press.
// Each list attaches its own listeners to its own root, so several lists on one page keep independent focus and selection.
// Keys and clicks that start in an interactive descendant (a text input, a checkbox, a button, a link, a menu) are left to
// that element.

const interactiveDescendant =
  "input, textarea, select, button, a[href], label, summary, [contenteditable]:not([contenteditable='false']), [role='menu'], [role='menuitem'], [role='menubar'], [role='listbox'], [role='option'], [data-list-interactive]";

function isTextEntry(element) {
  return element instanceof Element && element.closest("input, textarea, select, [contenteditable]:not([contenteditable='false'])") !== null;
}

export function attach(root, dotNet, options) {
  const multiple = options.selectionMode === "Multiple";
  const state = { activeIndex: -1 };

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
    if (event.key !== "Escape" || event.defaultPrevented || state.activeIndex < 0) return;
    const target = event.target instanceof Element ? event.target : null;
    if (target === null || isTextEntry(target) || target.closest("dialog[open]") !== null) return;
    const scope = options.escapeScopeId ? document.getElementById(options.escapeScopeId) : null;
    if (!root.contains(target) && (scope === null || !scope.contains(target))) return;
    event.preventDefault();
    const returnIndex = state.activeIndex;
    dotNet.invokeMethodAsync("CloseFromKeyboard").then(() => focusAt(returnIndex));
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

  const onFocusIn = (event) => {
    const row = rowOf(event.target);
    if (row !== null && event.target === row) setTabStop(row);
  };

  root.addEventListener("keydown", onKeyDown);
  root.addEventListener("mousedown", onMouseDown);
  root.addEventListener("click", onClick);
  root.addEventListener("focusin", onFocusIn);
  document.addEventListener("keydown", onDocumentKeyDown);

  return {
    // Called after every render: rows the grid rendered again have lost their tab index
    sync: (activeIndex, headerSelection) => {
      state.activeIndex = activeIndex;
      const all = rows();
      const focused = all.find((row) => row === document.activeElement);
      const tabStop = focused ?? all[activeIndex] ?? all.find((row) => row.tabIndex === 0) ?? all[0];
      if (tabStop !== undefined) setTabStop(tabStop);
      const selectAll = root.querySelector("input[data-list-select-all]");
      if (selectAll !== null) selectAll.indeterminate = headerSelection === "Some";
    },
    focusRow: (index) => focusAt(index) >= 0,
    dispose: () => {
      root.removeEventListener("keydown", onKeyDown);
      root.removeEventListener("mousedown", onMouseDown);
      root.removeEventListener("click", onClick);
      root.removeEventListener("focusin", onFocusIn);
      document.removeEventListener("keydown", onDocumentKeyDown);
    }
  };
}
