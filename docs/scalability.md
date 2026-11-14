# Scaling and operational limits

The initial deployment is a single .NET application with a local SQLite database. A repeatable service-level benchmark now measures a larger dataset and concurrent circulation work; see [performance.md](performance.md) for the actual results. It does not establish production user capacity or persistent Blazor-circuit limits.

## Current measures

- Catalogue pagination limits result sizes.
- Read projections retrieve the fields needed for lists and dashboards.
- Indexes support ISBN/barcode lookups, member open loans, overdue dates, and reservation deadlines.
- Circulation writes use short transactions.
- Per-operation contexts prevent long-lived tracking in Blazor circuits.
- Cached UI lists are bounded and invalidated after changes.
- Notification deduplication avoids repeated records on every worker pass.
- A singleton asynchronous queue serializes local circulation writers before they acquire SQLite's write transaction. It releases before best-effort live notification delivery.
- Member reservation filtering and FIFO position calculation run in SQL; another member's reservation history is not materialized to build the list.
- Multiple-collection book graph reads use split queries to avoid collection cross-products.

SQLite allows one pending writer at a time. Keep transactions short and retry whole transactions after transient contention. The provider's asynchronous methods do not supply true asynchronous database I/O; using an async application interface does not change that limitation. [SQLite transactions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions), [async limitations](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async)

## Move to PostgreSQL or SQL Server

1. Add the selected EF Core provider and production connection configuration.
2. Generate a provider-specific migration set; do not reuse SQLite SQL blindly.
3. Review filtered indexes, timestamp/money mapping, case-insensitive searches, and foreign-key delete behavior.
4. Replace SQLite write serialization with the target database's appropriate locking or concurrency strategy. Keep member limits and reservation allocation atomic.
5. Run all integration tests against the real selected database, especially the competing-borrower and member-limit tests.
6. Migrate existing data with a verified backup, reconciliation counts, and rollback procedure.

## Multiple application instances

Interactive Server circuits consume server memory and maintain persistent connections. Capacity planning should measure concurrent circuits, memory, query latency, and reconnect behavior. Use the appropriate session/connection routing configuration and a SignalR scale-out solution for multiple instances.

Background jobs also need coordination. Use database claims/leases or a job queue so multiple workers do not process the same deadline independently. Idempotence and database constraints remain necessary even with coordination.

## Caching

Cache public catalogue summaries with short expirations and invalidate changed books. Never put private member data in a shared cache without a user-specific key. Availability displayed from a cache remains advisory; the checkout transaction always reads authoritative state. For multiple instances, use a distributed cache and explicit invalidation.

## Stronger message delivery

Persisted in-app notifications make missed pushes recoverable. If the application later promises external email delivery, add an outbox record in the circulation transaction and a retrying delivery worker. Track attempts and deduplicate delivery; an in-process event alone cannot guarantee delivery across a crash.

## Deployment preparation

The application includes an explicit administrator bootstrap, production HTTPS/Secure-cookie controls, trusted-proxy configuration, persistent data-protection keys, database readiness, session revalidation, and a SQLite snapshot/restore utility. Follow [deployment.md](deployment.md) for tested settings and operational steps. Retention, monitoring, certificates, host permissions, and the actual external deployment remain the operator's responsibility.
