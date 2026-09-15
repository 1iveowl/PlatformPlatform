---
paths: blazor/tests/**/*.mjs
description: Rules for measuring Blazor pages and setting budgets from medians of repeated samples with the conditions recorded
---

# Measurements

How the Blazor edition measures load size and timing, and how a budget is set and checked. A budget is a median over repeated samples under recorded conditions on the trimmed Release publish, never a single run, and it is never raised to make a run pass.

## Implementation

1. Measure on the trimmed Release publish served in Production behind the gateway: `start-stack --without-blazor-host`, `blazor-publish`, `blazor-serve`, then `blazor-harness <script>` one browser at a time (see the blazor-publish skill). A Development host measures the wrong bytes.
2. Take repeated samples, each in a fresh browser context, and discard a warm-up sample; report the median, the minimum and the maximum per page, profile and load (cold and warm). Seven samples with one warm-up is the default of `public-pages.mjs` and `interactive-load.mjs`.
3. Record the conditions with the numbers: browser and version, profile (unthrottled or throttled with the latency and bandwidth), samples and warm-ups, the observation interval after the load event, CPU throttling, the publish commit and the date. The script writes them into the JSON result under `.workspace/blazor-tests/` with a `--label`.
4. Set a budget from the baseline medians of the most demanding condition the harness can apply in every run (Chromium, throttled profile, cold load, applied to every page), leave headroom the baseline's spread justifies, and write the baseline figures, the commit and the reasoning in the comment above the budget constant.
5. Check the budget with `--check-budget` on the profile it was set on; a regression is fixed or the budget is re-decided in review. Never raise a number to make a run pass and never compare a throttled budget against an unthrottled run.
6. Count what the budget names: fresh network requests, transfer bytes from resource timing, first contentful paint and the load event from navigation start; and assert the invariants that must hold regardless of size, such as zero WebAssembly runtime requests on a public page and zero policy violations.
7. State an unavailable measurement as unavailable, with the reason (Firefox and WebKit have no network throttling in the automation library), instead of substituting another browser's number.

## Examples

### Example 1 - A Budget With Its Baseline

```javascript
// ✅ DO: frozen from medians, the conditions and headroom explained (blazor/tests/public-pages.mjs)
// The public-page budget, frozen from the baseline medians: Chromium, throttled profile, cold load, applied to every public
// page. A regression is fixed or the budget is re-decided in review; never raise these numbers to make a run pass.
// Baseline on the publish of f0a777509, 2026-09-14: the heaviest page (login-verify) had a transfer median of 182,989 bytes
// and the slowest first contentful paint median was 236 ms (signup-verify). Transfer varied by under 100 bytes between
// samples, so 200,000 bytes leaves about 17 KB for content growth; 300 ms covers the 224 to 244 ms sample range plus runner
// variance, since the emulated latency dominates the paint time on this profile.
const publicPageBudget = { transferBytes: 200_000, firstContentfulPaintMs: 300 };

// ❌ DON'T: a number from one run, with no conditions and no reasoning
const publicPageBudget = { transferBytes: 185_000, firstContentfulPaintMs: 240 };
```

### Example 2 - Samples and Conditions in the Result

```javascript
// ✅ DO: a warm-up discarded, the conditions written with the numbers (blazor/tests/public-pages.mjs)
const sampleCount = Number(options.samples);
const warmUpSamples = 1;
...
conditions: { samples: sampleCount, warmUpSamplesDiscarded: warmUpSamples, observeMsAfterLoad: observeMs, throttledProfile, cpuThrottling: "none" },
...
for (let sample = 0; sample < warmUpSamples + sampleCount; sample++) {
  const measured = await measurePage(pageDefinition, profile.name);
  if (sample >= warmUpSamples) samples.push(measured);
}

// ❌ DON'T: one sample, and a summary that is not a median
const measured = await measurePage(pageDefinition, "unthrottled");
result.pages[pageDefinition.name] = measured;
```
