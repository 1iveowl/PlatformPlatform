// The browser side of push notifications: what this browser supports, what the user has allowed, and the subscription
// itself. Loaded as a same-origin module through the import map, like every other browser module of this edition.
//
// Nothing here decides anything the user sees: the module reports facts and performs the two actions the section asks
// for, and the section's own class decides what to render from them. The subscription's keys never leave the browser
// except in the call that saves the subscription on the account API, which is what a push service needs to encrypt for.
//
// The identifier of the saved subscription is kept in this device's local storage, so a subscription the user revoked in
// the browser settings can be removed from the account on the next visit, when the browser no longer has it and only the
// stored identifier says which row it was. The same identifier is what says, to the account signed in next on this
// browser, that the subscription the browser still holds was another account's.

const storageKey = "blazor-push-subscription";

export function readSupport() {
  return "serviceWorker" in navigator && "PushManager" in window && "Notification" in window;
}

export function readPermission() {
  return "Notification" in window ? Notification.permission : "denied";
}

export function readUserAgent() {
  return navigator.userAgent;
}

export function readStoredSubscriptionId() {
  try {
    return localStorage.getItem(storageKey);
  } catch {
    // Storage is unavailable in this context; a revoked subscription is then removed the next time one is saved
    return null;
  }
}

export function storeSubscriptionId(subscriptionId) {
  try {
    if (subscriptionId === null) localStorage.removeItem(storageKey);
    else localStorage.setItem(storageKey, subscriptionId);
    return true;
  } catch {
    return false;
  }
}

export async function readSubscription() {
  if (!readSupport()) return null;

  const registration = await navigator.serviceWorker.getRegistration();
  if (!registration) return null;

  try {
    const subscription = await registration.pushManager.getSubscription();
    return subscription === null ? null : describe(subscription);
  } catch {
    // A browser can carry the push manager and still refuse to use it, which Firefox does when push is turned off in its
    // configuration. The device then has no subscription, the same as a browser that was never asked; letting the refusal
    // through would fail the whole preferences surface, because an interop error reaches the framework's error banner.
    return null;
  }
}

// Asks for the permission if it has not been answered, then subscribes this browser with the deployment's public key.
// The outcome is a word the caller's class turns into what the section shows: nothing here builds a message.
export async function subscribe(applicationServerKey) {
  if (!readSupport()) return { outcome: "unsupported" };

  const permission = await Notification.requestPermission();
  if (permission !== "granted") return { outcome: permission === "denied" ? "denied" : "dismissed" };

  const registration = await navigator.serviceWorker.ready;
  try {
    const subscription = await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: decodeKey(applicationServerKey) });
    return { outcome: "subscribed", subscription: describe(subscription) };
  } catch {
    // The browser has no push service it can reach, or it refused the key; the section reports that and stays off
    return { outcome: "failed" };
  }
}

// The account that made this browser's subscription is leaving the device. The stored identifier goes first and
// synchronously, because the document is on its way out and only what runs before the first await is certain to run; the
// unsubscribe is started and not waited for. What it does not finish, the next visit finishes: a subscription the signed
// in account does not own is unsubscribed when the notifications section is read.
export function forgetDevice() {
  storeSubscriptionId(null);

  unsubscribe().catch(() => {
    // This browser would not look at its push manager; the subscription it still holds is unsubscribed on the next visit
  });

  return true;
}

export async function unsubscribe() {
  if (!readSupport()) return false;

  const registration = await navigator.serviceWorker.getRegistration();
  const subscription = registration === undefined ? null : await registration.pushManager.getSubscription();
  if (subscription === null) return true;

  return subscription.unsubscribe();
}

function describe(subscription) {
  return {
    endpoint: subscription.endpoint,
    publicKey: encodeKey(subscription.getKey("p256dh")),
    authSecret: encodeKey(subscription.getKey("auth"))
  };
}

function encodeKey(key) {
  return key === null ? "" : btoa(String.fromCharCode(...new Uint8Array(key))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

// The application server key is the base64url public half of the deployment's signing key pair; every browser takes it as
// the bytes it encodes
function decodeKey(applicationServerKey) {
  const padded = applicationServerKey.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(applicationServerKey.length / 4) * 4, "=");
  return Uint8Array.from(atob(padded), (character) => character.charCodeAt(0));
}
