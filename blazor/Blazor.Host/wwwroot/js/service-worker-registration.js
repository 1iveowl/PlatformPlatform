// Registers the offline shell's service worker. Imported by wwwroot/js/Blazor.Host.lib.module.js only on a document the
// host marked data-interactive-surface, so a static public page neither downloads this module nor registers a worker.
//
// The worker's address and its scope are the path base, which this module derives from its own URL rather than from the
// document: base-uri 'none' makes the browser ignore <base href>, and this file is served at <path base>/js/. Registration
// failures are swallowed: the offline shell is an addition, and an application that cannot install one still works.

const scope = new URL("../", import.meta.url).pathname;
const workerUrl = `${scope}service-worker.js`;

export async function register() {
  if (!("serviceWorker" in navigator)) return null;

  try {
    return await navigator.serviceWorker.register(workerUrl, { scope, updateViaCache: "none" });
  } catch {
    // No offline shell on this device; every page still loads from the network
    return null;
  }
}
