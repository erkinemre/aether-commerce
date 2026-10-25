# ADR 002: Complete orders from durable reservation responses

Status: Accepted, pending database integration execution.

Problem: Orders remained Pending after successful delivery. An acknowledgement could be lost after Inventory committed, and retry responses lacked the business outcome.

Decision: Every valid replay returns the stored reservation state. Dispatcher validates the response and commits publication plus terminal order state in one fenced transaction. One message is claimed per tick so a sequential batch cannot routinely outlive its lease. Retry arithmetic uses bigint to avoid integer multiplication overflow.

Alternatives: A reverse outbox/event consumer is useful with multiple result subscribers, but adds a second delivery pipeline here. Synchronous checkout would couple request availability to Inventory. A distributed transaction is unnecessary.

Trade-offs: Response schema now matters to the worker; deploy Inventory before Orders. Duplicate flags are replaced by the typed InventoryOutcome. HTTP is still at-least-once. Requests remain asynchronous, and throughput is intentionally bounded. Permanent failures need later reconciliation tooling.

Verification: Compilation succeeded. Integration suite covers concurrent keys, overselling, terminal outcomes and replay with changed payload; Docker is required to execute it. No database correctness result is fabricated.
