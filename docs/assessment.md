# Assessment and prioritized improvement plan

## Discovery

Reviewed all 17 original files: three .NET projects, two Minimal API hosts, one Npgsql worker, SQL initialization, Compose, two Dockerfiles, CI, README, ADRs and manual/static test notes. The supplied directory contained no visible `.git` directory. There is no frontend, EF Core, customer identity, bookstore catalog or full checkout domain to preserve. Existing business behavior is single-SKU order creation and asynchronous stock reservation.

Baseline: Orders compiled successfully with .NET SDK 10.0.301. Inventory also compiled; its first restore reported NU1900 because the sandbox could not reach vulnerability metadata. Restoring with network access removed that warning. Docker and PostgreSQL executables were unavailable on this machine. No baseline executable automated tests existed.

## Priorities and outcome

| Priority | Before | After |
| --- | --- | --- |
| P0 | Public mutation endpoints and client-controlled tenant access | Separate client/service API keys; client key bound to configured tenant |
| P0 | Order remains Pending forever | Durable inventory outcome advances order and publishes outbox atomically |
| P1 | Duplicate delivery returns only flags | Replay returns durable reservation status and checks SKU/quantity |
| P1 | 25 sequential requests share a 30-second lease | One claim per tick; ownership-checked completion retained |
| P1 | No meaningful automated assertions | Real-process security tests and disposable-database concurrency suite |
| P1 | Demo database password embedded in Compose | Required environment password and independent keys |
| P2 | Only liveness and mixed errors | Readiness, ProblemDetails, trace IDs, body cap and rate limit |
| P2 | SQL helpers mixed into API bootstrap | Helper extracted; HTTP defaults shared without extra runtime framework |
| P2 | Teaching-only documentation | Accurate API/development/demo docs and focused ADRs |

## Verification on 2026-10-24

- Both final Release builds: successful, zero errors and zero warnings after restore refresh.
- Actual HTTP security suite: five tests passed; one PostgreSQL integration scenario skipped explicitly.
- NuGet vulnerability queries for both API projects, including transitive packages: no vulnerabilities reported by the feed at execution time. This is not a guarantee of absence.
- Docker image build, Compose deployment, PostgreSQL concurrency suite, outage/recovery and CI workflow: not executed locally because Docker/PostgreSQL were unavailable.
- No throughput/load benchmarks performed.

## Remaining issues

The project is still a focused backend demo, not a full e-commerce product. Shared API keys do not provide end-user identity or roles. Stock is global rather than tenant-specific. Database users in Compose share privileges; production deployment needs separate least-privilege identities and transport security. Inbox/outbox/idempotency data has no retention policy. Permanently bad events retry indefinitely; no dead-letter UI exists. Readiness checks database connectivity, not schema versions. Rate limiting is per-process and shared by endpoints, not distributed. Existing pending outbox events can settle after upgrade, but previously published pending orders require a reviewed reconciliation operation; no automatic repair alters historical data. There are no applied migrations because schema is unchanged.

The next critical validation is the PostgreSQL integration run. Add product capabilities incrementally after that evidence is available. Do not claim the application is production ready.
