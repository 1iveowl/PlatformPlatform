// Reports whether a toolbar is at least a threshold wide, in rem of the root font size, for the users page's filters
// (UsersFilterModel). Loaded as a same-origin module through the import map. A ResizeObserver watches the toolbar and .NET
// is told only when the answer changes; dispose disconnects the observer. Nothing here writes a style attribute.

const ON_TOOLBAR_WIDTH_CHANGED = "OnToolbarWidthChanged";

function remInPixels() {
  return parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
}

export function observeToolbarWidth(element, dotNet, thresholdRem) {
  let lastIsWide = null;
  const check = () => {
    const isWide = element.offsetWidth >= thresholdRem * remInPixels();
    if (isWide === lastIsWide) return;
    lastIsWide = isWide;
    dotNet.invokeMethodAsync(ON_TOOLBAR_WIDTH_CHANGED, isWide).catch(() => {
      // The component is gone or the runtime is leaving the document
    });
  };
  const observer = new ResizeObserver(check);
  observer.observe(element);
  check();
  return { dispose: () => observer.disconnect() };
}
