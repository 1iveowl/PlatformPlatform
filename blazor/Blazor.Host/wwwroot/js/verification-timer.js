// Counts down the verification code's validity and reveals the resend action on the static verification pages, without
// the WebAssembly runtime. The server renders the state at request time (seconds remaining, seconds until resend) and stays
// authoritative for expiry, attempts and resend limits; this module only changes what the page shows, measured from when
// the page loaded, so neither a client clock nor a client timestamp can extend validity. The host page loads it once with
// the nonce; it rescans after every enhanced navigation and stops the previous page's timer.
let activeTimer;

function formatDuration(totalSeconds) {
  return `${Math.floor(totalSeconds / 60)}:${String(totalSeconds % 60).padStart(2, "0")}`;
}

function stop() {
  if (activeTimer === undefined) return;
  clearInterval(activeTimer);
  activeTimer = undefined;
}

function start() {
  stop();
  const root = document.querySelector("[data-verification-timer]");
  if (root === null) return;

  const remainingAtLoad = Number(root.dataset.remainingSeconds);
  const resendInAtLoad = Number(root.dataset.resendInSeconds);
  const template = root.dataset.validForTemplate;
  const validFor = root.querySelector("[data-valid-for]");
  const expired = root.querySelector("[data-expired]");
  const resend = root.querySelector("[data-resend-reveal]");
  const loadedAt = performance.now();

  const update = () => {
    const elapsed = Math.floor((performance.now() - loadedAt) / 1000);
    const remaining = Math.max(0, remainingAtLoad - elapsed);
    validFor.textContent = template.replace("{0}", formatDuration(remaining));
    validFor.hidden = remaining === 0;
    expired.hidden = remaining > 0;
    if (elapsed >= resendInAtLoad) resend.hidden = false;
    if (remaining === 0 && !resend.hidden) stop();
  };

  update();
  activeTimer = setInterval(update, 1000);
}

start();
window.Blazor?.addEventListener("enhancedload", start);
