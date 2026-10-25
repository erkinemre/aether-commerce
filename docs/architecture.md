# Architecture

Orders owns order state, idempotency and outbox data. Inventory owns stock, reservations and inbox data. Contracts contains transport records only. Shared HTTP defaults are linked source, keeping the existing three project layout and Docker restore structure. There is no sufficiently rich domain model yet to justify separate Domain/Application projects or generic repositories over direct SQL.

The order transaction inserts the idempotency key, order and event together. Concurrent identical keys serialize through PostgreSQL uniqueness; differing payloads return 409. The initial response remains Pending on an idempotent replay, preserving the create operation's response semantics. GET is authoritative for current state.

Inventory inserts its inbox guard and order reservation guard, conditionally decrements stock, records Reserved/Rejected and commits. The business predicate `available >= quantity` is evaluated in the update under PostgreSQL row locking. Duplicate delivery reads the durable result; it cannot decrement stock again. Existing code comments explaining this non-obvious guard are retained.

The dispatcher validates the returned order ID and reservation state. It marks the event published and updates the Pending order in one Orders transaction, only if it still owns the lease. A crash before this commit causes retry; Inventory returns its existing result. No distributed transaction or exactly-once transport is claimed. Rejected stock is a terminal business result, not a transport failure. Missing SKU also rejects.

No CQRS framework, domain events beyond the existing transport contract, specification pattern, cache, Redis or message broker is warranted by the current implementation. Cancellation propagates through HTTP and database operations. Existing indexed polling and conditional updates are retained. Single-message claims exchange throughput for predictable lease timing. Measure before adding bounded parallelism or renewal.

See the ADRs for rejected alternatives, compatibility impacts and verification limits.
