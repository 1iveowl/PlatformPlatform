// Spike code (Blazor edition, stage B1): opt-in with ?b1-apex-nonce=1. ApexCharts (bundled in Blazor-ApexCharts 7.0.0)
// reads chart.nonce from the window.Apex global options and puts it on the <style> elements it injects; the C# Chart
// options of the wrapper expose no Nonce property. The nonce comes from this script's own element.
window.Apex = { ...window.Apex, chart: { ...window.Apex?.chart, nonce: document.currentScript.nonce } };
