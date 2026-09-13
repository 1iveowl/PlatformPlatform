// Spike code (Blazor edition, stage B1): records the moment the WebAssembly component became interactive.
export function markInteractive() {
  window.__b1InteractiveAt = performance.now();
  document.documentElement.dataset.blazorInteractive = "true";
}
