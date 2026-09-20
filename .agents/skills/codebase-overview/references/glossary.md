# Glossary

Terms that appear in code or commits. Use them consistently.

- **SCS, self-contained system**: one small monolith under `application/` (account, main) with its own database and
  OpenAPI document.
- **shared-kernel**: the SharedKernel library and shared test toolkit.
- **AppHost**: the Aspire orchestrator that starts everything locally, allocates ports and holds parameters.
- **AppGateway**: the YARP reverse proxy in front of the APIs and static servers.
- **Back office**: the internal admin SPA (`account/BackOffice`), Easy Auth in Azure, `MockEasyAuth` locally.
- **Tenant**: a customer account. A person in several tenants has several User rows with the same email.
- **Owner, Admin, Member**: the user roles. E2E fixtures expose `ownerPage`, `adminPage`, `memberPage`.
- **Email login**: passwordless one-time-password flow (`EmailLogin` aggregate). The code is `UNLOCK` on localhost.
- **External login**: OAuth or OIDC flow tracked by the `ExternalLogin` aggregate from start redirect to callback.
- **ExternalProviderType**: enum of providers (Google, Entra, MitId). Its lower-cased name is the keyed service name.
- **ExternalIdentity**: a tenant-scoped row in `external_identities` linking a user to one provider identity, keyed
  by provider and provider user id.
- **Identifier-first lookup**: resolving a returning user by provider and provider user id before falling back to
  email.
- **Verification flow**: an external flow started from an authenticated session that binds a provider identity and
  its evidence to the current user. Not a login (`StartExternalVerification`, `CompleteExternalVerification`).
- **LoginMethod**: how a Session was created, email or a provider.
- **Session**: the refresh token record with rotating jti, device type, login method and revocation.
- **Mock provider**: `MockOAuthProvider`, selected by the `__Test_Use_Mock_Provider` cookie when mock providers are
  allowed; never in Azure.
- **Unfiltered**: suffix on repository methods that bypass the tenant query filter.
- **Result**: return type of every command and query; `ApiResult` maps it to HTTP.
- **port.txt**: `.workspace/port.txt`, the base port of a checkout; offsets in `PortAllocation.cs`.
