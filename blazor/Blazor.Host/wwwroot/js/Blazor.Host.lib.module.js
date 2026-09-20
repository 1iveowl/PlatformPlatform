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

// The offline shell's worker is registered from the same marker, and from nothing else, so a static public page neither
// downloads the registration module nor installs a worker. The registration is not awaited by the start-up path: an
// offline shell is an addition, and a page must never wait for one.
function registerOfflineShell() {
  import("./service-worker-registration.js")
    .then((module) => module.register())
    .catch(() => {
      // No offline shell on this device; every page still loads from the network
    });
}

export async function beforeWebStart(options) {
  if (!isInteractiveSurface()) return;
  registerOfflineShell();
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
