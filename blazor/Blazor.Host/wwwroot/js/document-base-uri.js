// The policy sends base-uri 'none', so the browser ignores <base href> and document.baseURI is the document URL.
// blazor.web.js resolves dotnet.js, the JS initializers, "./" module imports and the NavigationManager base against
// document.baseURI, which breaks every route below the path base. This script, loaded with the nonce before
// blazor.web.js, exposes the path base the host declares in data-base as document.baseURI. Native URL resolution (links,
// fetch, import maps) still uses the document URL, which is why the host renders every URL root-absolute.
// Replacing base-uri 'none' with base-uri 'self' would remove the need for this script, but weakens the policy.
const declaredBase = new URL(document.currentScript.dataset.base, location.origin).href;
Object.defineProperty(document, "baseURI", { configurable: true, get: () => declaredBase });
