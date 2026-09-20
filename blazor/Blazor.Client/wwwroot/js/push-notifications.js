// The browser side of push notifications: what this browser supports, what the user has allowed, and the subscription
// itself. Loaded as a same-origin module through the import map, like every other browser module of this edition.
//
// Nothing here decides anything the user sees: the module reports facts and performs the two actions the section asks
// for, and the section's own class decides what to render from them. The subscription's keys never leave the browser
// except in the call that saves the subscription on the account API, which is what a push service needs to encrypt for.
//
// The identifier of the saved subscription is kept in this device's local storage, so a subscription the user revoked in
// the browser settings can be removed from the account on the next visit, when the browser no longer has it and only the
// stored identifier says which row it was.

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

  const subscription = await registration.pushManager.getSubscription();
  return subscription === null ? null : describe(subscription);
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
