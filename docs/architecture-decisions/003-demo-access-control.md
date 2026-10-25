# ADR 003: Explicit demo access control

Status: Accepted for local portfolio demonstrations.

Problem: Any caller could create orders, impersonate tenants or mutate inventory through the internal event endpoint.

Decision: Fail startup without configured keys of at least 32 characters. Use separate client and service secrets, fixed-time digest comparison, a configured client tenant, generic error bodies and request bounds. Health checks remain unauthenticated. No credentials appear in source or request logs.

Alternatives: OIDC/JWT with tenant membership and roles is the preferred customer identity direction. Introducing a bespoke password store is inappropriate for this scope. mTLS is a deployment concern rather than an application-only change.

Trade-offs: Existing API clients must send X-Api-Key. The demo represents one client tenant, has no user accounts or RBAC, and keys require secure transport outside loopback. Application defaults cannot enforce every hosting security property.

Verification: Missing/wrong credentials, client/service separation, cross-tenant reads/writes and input validation were tested against running APIs.
