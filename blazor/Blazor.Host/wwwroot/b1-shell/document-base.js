// Spike code (Blazor edition, stage B1): base-uri 'none' makes the browser ignore <base href>, so document.baseURI is the
// document URL. blazor.web.js resolves dotnet.js, JS initializers, "./" module imports and the NavigationManager base
// against document.baseURI, which breaks every route below the path base. This exposes the path base the host
// declares on this script tag as document.baseURI to scripts. Native URL resolution (links, fetch, import maps)
// still uses the document URL, which is why the host page renders absolute URLs.
const declaredBase = new URL(document.currentScript.dataset.base, location.origin).href;
Object.defineProperty(document, "baseURI", { configurable: true, get: () => declaredBase });
