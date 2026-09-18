// Reports which of the edition's five breakpoints the viewport currently reaches, for the decisions that cannot be made in
// CSS because the DOM itself must change: a side pane that becomes a modal dialog, a list that loads a page at a time.
// Everything that is only a matter of layout stays a media query in the host stylesheet and never reaches .NET.
//
// Loaded as a module from the same origin through the import map, so it runs under the policy without a nonce. One
// ViewportState attaches once per runtime and disposes the handle with the container, so navigating between pages never
// adds a second set of listeners. Nothing here writes a style attribute.

// The literals Breakpoints holds; BreakpointsTests fails when they drift from it or from app.css
const queries = {
  small: "(min-width: 40rem)",
  medium: "(min-width: 48rem)",
  large: "(min-width: 64rem)",
  extraLarge: "(min-width: 80rem)",
  extraExtraLarge: "(min-width: 96rem)"
};

const ON_VIEWPORT_CHANGED = "OnViewportChanged";

function matchesOf(mediaQueryLists) {
  return {
    small: mediaQueryLists.small.matches,
    medium: mediaQueryLists.medium.matches,
    large: mediaQueryLists.large.matches,
    extraLarge: mediaQueryLists.extraLarge.matches,
    extraExtraLarge: mediaQueryLists.extraExtraLarge.matches
  };
}

export function attachViewport(dotNet) {
  const abort = new AbortController();
  const mediaQueryLists = {
    small: window.matchMedia(queries.small),
    medium: window.matchMedia(queries.medium),
    large: window.matchMedia(queries.large),
    extraLarge: window.matchMedia(queries.extraLarge),
    extraExtraLarge: window.matchMedia(queries.extraExtraLarge)
  };

  const onChange = async () => {
    try {
      await dotNet.invokeMethodAsync(ON_VIEWPORT_CHANGED, matchesOf(mediaQueryLists));
    } catch {
      // The runtime is leaving the document; the next one reads the widths again on attach
    }
  };

  for (const mediaQueryList of Object.values(mediaQueryLists)) mediaQueryList.addEventListener("change", onChange, { signal: abort.signal });

  return {
    read: () => matchesOf(mediaQueryLists),
    dispose: () => abort.abort()
  };
}
