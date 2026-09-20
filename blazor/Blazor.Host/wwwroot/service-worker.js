// The offline shell's service worker. Registered only from a document the host marked as an interactive surface (see
// wwwroot/js/service-worker-registration.js), so a visitor who only ever sees the public pages never installs one.
//
// It is served by Blazor.Host.HostApplication rather than by the static asset pipeline, with Cache-Control: no-cache and
// Service-Worker-Allowed for the path base, and the four values below are substituted into this source by
// Shell/OfflineShell.BuildWorkerScript. The cache name carries the client version the release policy compares, so a
// deployment that changes that version installs a new worker whose activation drops everything the previous one stored.
//
// What may be stored, and nothing else:
// - exactly one document, the anonymous offline shell, fetched with credentials omitted at install and again after any
//   successful navigation into the authenticated surface that finds none stored, so no user's or tenant's rendering can
//   ever reach the cache and a departure that dropped the shell does not leave the next session without one;
// - subresources under the path base whose own response says they may be stored (neither no-store nor no-cache). Every
//   component document is no-store, the web manifest and this worker are no-cache, and the account API is outside the
//   path base, so no document, no bootstrap, no API response and no token can enter by this route.
//
// What may be answered from it:
// - a navigation whose first path segment is one of the authenticated surface's, and only after the network has failed.
//   The landing page, login, signup, the verification pages, the legal documents, the status pages and every external
//   authentication callback are not intercepted at all and always go to the network.
// - a stored subresource, cache first, which is safe because a stored subresource is either fingerprinted or revalidated
//   content that carries no identity.
//
// Logout, a session that ended and a tenant switch post clearOfflineShell, which empties the document cache and keeps the
// asset cache. The cache holds no identity by construction; the message is the second guard.

const pathBase = "__PATH_BASE__";
const shellDocument = "__SHELL_DOCUMENT__";
const appSegments = "__APP_SEGMENTS__".split(",");
const cacheVersion = "__CACHE_VERSION__";

const cachePrefix = "blazor-offline-";
const documentCacheName = `${cachePrefix}document-${cacheVersion}`;
const assetCacheName = `${cachePrefix}assets-${cacheVersion}`;
const clearMessageType = "clearOfflineShell";

// The shell is fetched without credentials, so what is stored cannot depend on who was signed in when it was stored
async function storeShellDocument() {
  const response = await fetch(new Request(shellDocument, { credentials: "omit", cache: "reload" }));
  if (!response.ok) throw new Error(`The offline shell answered ${response.status}.`);
  await (await caches.open(documentCacheName)).put(shellDocument, response);
}

// Installing is not the only moment a shell is needed: a departure drops the stored one, and the worker that is already
// activated never installs again, so the next session would have no shell at all. Every successful navigation into the
// authenticated surface puts one back when there is none, and never while one is there.
let storingShell = null;

function restoreShellDocument() {
  storingShell ??= caches
    .match(shellDocument, { cacheName: documentCacheName })
    .then((cached) => (cached === undefined ? storeShellDocument() : undefined))
    .catch(() => {
      // No shell this time; the next navigation into the authenticated surface tries again
    })
    .finally(() => {
      storingShell = null;
    });
  return storingShell;
}

async function dropOtherVersions() {
  const names = await caches.keys();
  await Promise.all(names.filter((name) => name.startsWith(cachePrefix) && name !== documentCacheName && name !== assetCacheName).map((name) => caches.delete(name)));
}

async function clearShellDocument() {
  await caches.delete(documentCacheName);
}

function isSameOrigin(url) {
  return url.origin === self.location.origin;
}

function isUnderPathBase(url) {
  return url.pathname.startsWith(pathBase);
}

function isAppNavigation(url) {
  return isUnderPathBase(url) && appSegments.includes(url.pathname.slice(pathBase.length).split("/")[0]);
}

// A response that names itself unstorable is never stored: every component document is no-store, and the web manifest and
// this worker are no-cache
function mayBeStored(response) {
  if (!response.ok || response.status !== 200 || response.type !== "basic") return false;

  const cacheControl = (response.headers.get("cache-control") ?? "").toLowerCase();
  return cacheControl.length > 0 && !cacheControl.includes("no-store") && !cacheControl.includes("no-cache");
}

async function answerNavigation(request) {
  try {
    const response = await fetch(request);
    if (response.ok) restoreShellDocument();
    return response;
  } catch (networkError) {
    const cached = await caches.match(shellDocument, { cacheName: documentCacheName });
    if (cached === undefined) throw networkError;
    return cached;
  }
}

async function answerAsset(request) {
  const cache = await caches.open(assetCacheName);
  const cached = await cache.match(request);
  if (cached !== undefined) return cached;

  const response = await fetch(request);
  if (mayBeStored(response)) await cache.put(request, response.clone());
  return response;
}

self.addEventListener("install", (event) => {
  event.waitUntil(storeShellDocument().then(() => self.skipWaiting()));
});

self.addEventListener("activate", (event) => {
  event.waitUntil(dropOtherVersions().then(() => self.clients.claim()));
});

self.addEventListener("message", (event) => {
  if (event.data?.type !== clearMessageType) return;

  const reply = event.ports?.[0];
  event.waitUntil(clearShellDocument().then(() => reply?.postMessage({ type: clearMessageType, cleared: true })));
});

self.addEventListener("fetch", (event) => {
  const request = event.request;
  if (request.method !== "GET") return;

  const url = new URL(request.url);
  if (!isSameOrigin(url)) return;

  if (request.mode === "navigate") {
    if (isAppNavigation(url)) event.respondWith(answerNavigation(request));
    return;
  }

  // A caller that asked the network for fresh bytes gets them; the runtime uses this when it revalidates its own files
  if (!isUnderPathBase(url) || request.cache === "no-store" || request.cache === "reload") return;

  event.respondWith(answerAsset(request));
});
