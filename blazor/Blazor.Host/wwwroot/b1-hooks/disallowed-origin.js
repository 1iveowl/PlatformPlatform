// Spike code (Blazor edition, stage B1): CSP test hook loaded from an origin outside the policy's host list; expected to be blocked.
document.documentElement.dataset.b1DisallowedOriginScript = "ran";
