# API reference

JSON uses camelCase. The API has no generated Swagger UI. This document is the current endpoint contract.

| Method/path | Access | Result |
| --- | --- | --- |
| GET /health/live | Public | 200 if process serves requests |
| GET /health/ready | Public | 200 for DB connectivity; 503 otherwise |
| POST /orders | Client X-Api-Key | 201 new; 200 identical replay; 409 key conflict |
| GET /orders/{id}?tenantId=demo | Client X-Api-Key | 200 current state; 404 absent; 403 other tenant |
| POST /internal/order-placed | Service X-Api-Key | Durable InventoryOutcome; 409 conflicting replay |
| GET /internal/stock/{sku} | Service X-Api-Key | Available/reserved quantities; 404 absent |

Order body: `{"tenantId":"demo","skuId":"11111111-1111-1111-1111-111111111111","quantity":2}`. Required Idempotency-Key: one nonblank value of at most 128 characters. Quantity: 1–1000. Tenant: configured tenant, at most 80 characters. SKU must be nonempty UUID. A single idempotency key applies to one payload and one tenant indefinitely (no expiry policy yet).

Create response: `{"orderId":"<uuid>","status":"Pending"}`. Replaying creation retains this initial status; GET reports current status (`Pending`, `Confirmed`, `Rejected`). The Location includes tenantId. Confirmed means inventory reserved only.

Internal event fields: eventId, orderId, tenantId, skuId, quantity, occurredAt. All identifiers must be nonempty, tenant nonblank and quantity bounded. Result: `{"orderId":"<uuid>","reservationStatus":"Reserved"}` or `Rejected`. Unknown SKU is Rejected. Duplicate deliveries return that same durable outcome. Distinct event IDs for the same order do not reserve again. Events are trusted service inputs; this is not a public shopping endpoint.

Invalid credentials return 401. Wrong tenant returns 403. Invalid input returns 400. Application-generated errors use ProblemDetails; some framework binding failures may have an empty body. Global exception responses omit implementation details and include traceId. Bodies are capped at 16 KiB. Business endpoints share a per-process 120-request/minute fixed window and return 429 when exhausted. Health is not rate limited. No Retry-After contract is provided.

Compatibility changes: credentials are required, duplicate event acknowledgements now return InventoryOutcome, and orders may reach terminal states. Deploy Inventory before the new worker. Stock remains global across tenant orders.
