// Remembers the tenant a user switched to, so the static login verification page can send it as the preferred tenant on
// the next login. The cookie holds a tenant id and nothing else; the account API only honours it for an active membership.
export function remember(cookieName, tenantId, maxAgeSeconds) {
  document.cookie = `${cookieName}=${encodeURIComponent(tenantId)}; Path=/; Max-Age=${maxAgeSeconds}; Secure; SameSite=Lax`;
}
