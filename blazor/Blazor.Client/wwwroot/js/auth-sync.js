// The auth-sync channel between the tabs of one browser for AuthSyncCoordinator. Loaded as a same-origin module through the
// import map. It forwards channel messages and asks .NET to reconcile when the document becomes visible, gains focus or is
// restored from the back/forward cache; which message changes what is decided in AuthSyncRules, never here. Messages carry
// identifiers only: no credential, one-time password or antiforgery token is ever posted, and nothing is stored. Where
// BroadcastChannel is missing or refuses to open, no message is sent or received and the visibility, focus and restoration
// reconciliation is the fallback. Listeners are attached with addEventListener and removed by dispose.

const channelName = "auth-sync";

const ON_MESSAGE = "OnMessage";
const ON_RECONCILE = "Reconcile";

// Identifiers arrive as strings from either edition; an empty value names nothing
function identifier(value) {
  if (typeof value === "number" && Number.isFinite(value)) return String(value);
  return typeof value === "string" && value.length > 0 ? value : null;
}

function openChannel() {
  try {
    return typeof BroadcastChannel === "function" ? new BroadcastChannel(channelName) : null;
  } catch {
    return null;
  }
}

export function attach(dotNet, announcement) {
  const channel = openChannel();

  const invoke = (method, ...args) => {
    dotNet.invokeMethodAsync(method, ...args).catch(() => {
      // The runtime is gone; the next document attaches again
    });
  };

  const onMessage = (event) => {
    const data = event.data;
    if (data === null || typeof data !== "object" || typeof data.type !== "string" || typeof data.timestamp !== "number") return;
    invoke(ON_MESSAGE, {
      type: data.type,
      userId: identifier(data.userId),
      tenantId: identifier(data.tenantId),
      newTenantId: identifier(data.newTenantId),
      previousTenantId: identifier(data.previousTenantId),
      tenantName: typeof data.tenantName === "string" ? data.tenantName : null,
      email: identifier(data.email),
      timestamp: Math.trunc(data.timestamp)
    });
  };

  const onVisibilityChange = () => {
    if (document.visibilityState === "visible") invoke(ON_RECONCILE);
  };
  const onFocus = () => invoke(ON_RECONCILE);
  const onPageShow = (event) => {
    if (event.persisted) invoke(ON_RECONCILE);
  };

  const post = (message) => {
    if (channel === null) return;
    const payload = {};
    for (const [key, value] of Object.entries(message)) {
      if (value !== null && value !== undefined && key !== "timestamp") payload[key] = value;
    }
    payload.timestamp = Date.now();
    try {
      channel.postMessage(payload);
    } catch {
      // A closed channel: the other tabs reconcile when they are next shown or focused
    }
  };

  channel?.addEventListener("message", onMessage);
  document.addEventListener("visibilitychange", onVisibilityChange);
  window.addEventListener("focus", onFocus);
  window.addEventListener("pageshow", onPageShow);

  if (announcement) post(announcement);

  return {
    post,
    dispose: () => {
      channel?.removeEventListener("message", onMessage);
      channel?.close();
      document.removeEventListener("visibilitychange", onVisibilityChange);
      window.removeEventListener("focus", onFocus);
      window.removeEventListener("pageshow", onPageShow);
    }
  };
}
