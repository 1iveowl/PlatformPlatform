// Reads the fingerprinted assets this document was built with, and asks the server whether one of them is still there.
// A deployment replaces the fingerprinted route of every changed asset, so the route this document names answers 404 once
// the publish it belongs to is no longer served, which is how a long-lived tab learns that its release is gone.
//
// Loaded as a same-origin module through the import map, so it runs under the policy's script-src-elem host list without a
// nonce. Nothing is decided here: which of the URLs carries a fingerprint and what a status means are decided in
// FingerprintedAsset and StaleAssetProbe. The request must not be answered from the browser's cache, because a
// fingerprinted asset is cached for a year and would answer 200 long after the server stopped serving it.

// Every asset of this edition the document has loaded, from resource timing, plus the stylesheets and scripts it names.
// Resource timing is what makes the runtime files visible: the host page names none of them, and it is the assemblies whose
// route changes when the code does.
export function readAssetUrls(pathBase) {
  const prefix = `${location.origin}${pathBase}/`;
  const loaded = performance.getEntriesByType("resource").map((entry) => entry.name).filter((name) => name.startsWith(prefix));
  const links = [...document.querySelectorAll('link[rel="stylesheet"][href]')].map((link) => link.getAttribute("href"));
  const scripts = [...document.querySelectorAll("script[src]")].map((script) => script.getAttribute("src"));
  return [...loaded, ...links, ...scripts].filter((url) => typeof url === "string" && url.length > 0);
}

export async function requestStatus(url) {
  try {
    const response = await fetch(url, { method: "HEAD", cache: "no-store" });
    return response.status;
  } catch {
    // Offline or the request was blocked: nothing is learned about the publish
    return null;
  }
}
