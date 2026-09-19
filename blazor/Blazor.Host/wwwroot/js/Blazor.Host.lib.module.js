// The host's JavaScript initializer, which blazor.web.js imports on every document. It stands in for the component
// library's own initializer, which Blazor.Host.csproj keeps out of this project's JS module manifest: that file is the
// library's whole browser bundle, about 105 KB compressed, and importing it on every document made every static public
// page pay for a library no page outside the authenticated WebAssembly surface uses.
//
// The bundle is imported here instead, on the two occasions a library component can appear: a document the host marked as
// an interactive surface, and a WebAssembly runtime start, which is how an enhanced navigation reaches such a page from a
// page that carried no marker. The library's own beforeStart and afterStarted are guarded against running twice, so the
// two paths cannot initialize it twice. The import is relative to this file, which the SDK's naming convention keeps named
// after the project and which the host serves beside _content below the path base, so it needs no base URL of its own.

let componentLibrary = null;

async function loadComponentLibrary() {
  componentLibrary ??= await import("../_content/Microsoft.FluentUI.AspNetCore.Components/Microsoft.FluentUI.AspNetCore.Components.lib.module.js");
  return componentLibrary;
}

// Set by Components/App.razor on a page carrying [InteractiveSurface], the same attribute that decides the runtime preloads
function isInteractiveSurface() {
  return document.documentElement.hasAttribute("data-interactive-surface");
}

export async function beforeWebStart(options) {
  if (!isInteractiveSurface()) return;
  (await loadComponentLibrary()).beforeWebStart?.(options);
}

// Only forwarded when beforeWebStart already loaded the bundle: a static public page must not fetch it here either
export function afterWebStarted(blazor) {
  componentLibrary?.afterWebStarted?.(blazor);
}

export async function beforeWebAssemblyStart(options, extensions) {
  (await loadComponentLibrary()).beforeWebAssemblyStart?.(options, extensions);
}

export async function afterWebAssemblyStarted(blazor) {
  (await loadComponentLibrary()).afterWebAssemblyStarted?.(blazor);
}
