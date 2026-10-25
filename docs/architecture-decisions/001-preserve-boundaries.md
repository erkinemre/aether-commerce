# ADR 001: Evolve the existing service boundaries

Status: Accepted.

Problem: A small two-service lab is being developed into a credible portfolio backend. Replacing it wholesale with a new layered monolith would discard its integration and failure semantics.

Decision: Preserve Orders/Inventory databases, HTTP contract and transactional SQL. Extract shared hosting policy and SQL helpers only. A modular monolith remains the preferred starting point for a new product of this size; it is not retrofitted solely for architectural appearance.

Alternatives: Consolidating databases simplifies operation but requires data migration and changes failure boundaries. Adding a broker introduces infrastructure without measured demand. Generic repositories, MediatR/CQRS and a new domain assembly add little value to this small workflow.

Trade-offs: Two processes and eventual consistency remain operational costs. Transport contracts are shared at compile time. More complex business rules should motivate a domain layer later.

Verification: Both API builds and security suite executed. Database integration checks are supplied but not executed locally.
