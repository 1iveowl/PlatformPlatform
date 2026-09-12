# Check 6: branch versus main, 2026-09-12

Each task was run once in a session on `main` and once in a session on `experiment/05-ai-usage`. Context is the session's final context size in tokens; words is the length of the final message.

| Task | Main ctx / words | Branch ctx / words |
|---|---|---|
| read-heavy lookup #1 (OTP validity/lockout) | 54,223 / 221 | 63,684 / 231 |
| run account tests | 51,369 / 49 | 48,547 / 18 |
| feature-flag admin action orientation | 61,963 / 429 | 67,816 / 407 |
| read-heavy lookup #2 (login lockout) | 58,771 / 335 | 66,619 / 435 |
| Median | 56,497 / 278 | 65,151.5 / 319 |

## Finding

The literal success criterion (smaller context and shorter final message than main) was not met in aggregate: the branch medians are higher on both measures. CLI output trimming reduces context and message size on tool-heavy tasks: the test-running task ended with a smaller context and an 18-word final message against 49 on main. Instruction trimming (3a/3b) did not show a context-size reduction on read-heavy lookup tasks in this sample, and was split on the orientation task. The sample is n=4 with a single run per cell, so it is not statistically conclusive. Instruction trimming is kept anyway, because it independently improved the EP-64 behaviour-check pass rate and cut the always-loaded token count, which were this slice's other goals. This is recorded as a known limitation, not a blocker.
