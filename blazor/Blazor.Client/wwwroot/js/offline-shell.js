// The authenticated surface's side of the offline shell: it tells the service worker to drop the stored shell document when
// the session of this runtime ends. Loaded as a same-origin module through the import map, like every other browser module
// of this edition, and imported while the surface is alive so the message can still be posted synchronously from the
// moment the session ends, shortly before the full document navigation starts.
//
// Nothing is read back and nothing is stored here. The message names an action and nothing else: no identifier, no token
// and no tenant. The worker holds no identity to begin with, because only the anonymous shell document and subresources
// that say they may be stored ever enter its caches; this is the second guard, so no previous user's or tenant's shell is
// what the next launch of the installed application shows.

const clearMessageType = "clearOfflineShell";

export function clearShell() {
  const controller = navigator.serviceWorker?.controller;
  if (!controller) return false;

  try {
    controller.postMessage({ type: clearMessageType });
    return true;
  } catch {
    // The worker is gone or redundant; a worker installed later stores a shell of its own
    return false;
  }
}
