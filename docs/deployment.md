# Operating BookBridge

The local launchers deliberately run the seeded Development demonstration. A hosted library must use `Production`, its own database, its own administrator, persistent key storage, and HTTPS. The application now enforces production key storage and HTTPS, ignores the demo-seed flag in Production, and supports an explicit first-administrator bootstrap. These controls and backup/restore are covered by integration tests; deployment to an external server has not been performed.

## Build and initialize

Use .NET 8 and run the commands from the solution directory:

```sh
dotnet restore --locked-mode
dotnet publish src/LibraryLending.Web -c Release -o artifacts/publish
dotnet tool restore
```

Configure these settings through the hosting platform's environment/secrets mechanism. Double underscores are the environment-variable form of a configuration colon.

| Setting | Production value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__Library` | `Data Source=/absolute/persistent/path/library.db;Default Timeout=5` (use an appropriate Windows path on Windows) |
| `DataProtection__KeyPath` | An absolute persistent directory readable/writable only by the app's service account and authorized backup operators |
| `Bootstrap__Enabled` | `true` only during initial administrator creation |
| `Bootstrap__Email` | The real initial librarian's email |
| `Bootstrap__Password` | A unique secret meeting Identity's password policy; never the public demo password |
| `AllowedHosts` | The actual public hostname |
| `Database__ApplyMigrations` | `false` normally; use `true` for a controlled first initialization or maintenance startup |

For an existing database, take a verified backup before migration. Apply migrations during a maintenance window using `dotnet ef database update --project src/LibraryLending.Infrastructure --startup-project src/LibraryLending.Web`, with the same production settings. Alternatively start one application instance with `Database__ApplyMigrations=true`, then disable it after initialization. Default production startup rejects pending migrations.

Bootstrap creates one librarian only in an empty librarian role. It never resets an existing password or promotes an existing member account. Once the first administrator can sign in, remove the bootstrap password/email from the service environment and set `Bootstrap__Enabled=false`. Do not pass passwords on a command line or put them in committed configuration. Development credentials are never automatically provisioned in Production.

## HTTPS, proxy, keys, and sessions

Terminate TLS at a configured reverse proxy, or configure Kestrel with the host's certificate and HTTPS endpoint. `Hosting__HttpsPort` defaults to `443`; set it if a different public HTTPS port is used. The application redirects HTTP in Production, sends HSTS, and requires Secure/HttpOnly authentication cookies.

Behind a reverse proxy, set `Hosting__KnownProxies__0` (and subsequent indices if needed) to the explicit proxy IP. Forwarded protocol/client headers are ignored unless a trusted proxy is configured. Do not accept forwarded headers from arbitrary clients. Keep the application listener restricted to the proxy where appropriate.

The persistent data-protection key ring preserves cookie/token protection across restarts. For encryption at rest, supply `DataProtection__CertificatePath` and secret `DataProtection__CertificatePassword` for a certificate available to the service account. Restrict directory permissions even when keys are encrypted. Back up the key ring and certificate separately from the database. Persistent keys without an explicit encryption mechanism rely on filesystem protection. See [Microsoft's key-storage guidance](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-8.0).

Interactive sessions revalidate Identity's security stamp and current roles every five minutes using a fresh scope. A password/security-stamp change or revoked role invalidates the old circuit on its next check. Standard cookie security-stamp validation also remains enabled. This is bounded revalidation, not an instantaneous remote logout promise.

## Health and logs

- `/health` checks that the web process is serving requests.
- `/health/ready` checks connectivity, migration state, and a catalogue-table query. It returns HTTP 503 when the database is unavailable; it does not expose connection strings or SQL errors.
- Background job failures are logged and retried on a later worker interval. Persisted notifications survive a failed live push and are available on the next page refresh.

Route health probes through HTTPS in Production. Collect application logs in the hosting environment and alert on failed readiness probes and repeated deadline-worker errors. Do not enable sensitive EF data logging for real member records.

## Consistent backup and tested restore

The database utility uses SQLite's backup API so committed WAL records are included. Copying only the live `.db` file is insufficient when writes remain in a WAL. SQLite backup may briefly block another writer; schedule larger snapshots during low activity. [SQLite backup documentation](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup)

```sh
dotnet run --project tools/LibraryLending.DatabaseTool -- backup /data/library.db /backups/library-2026-11-14.db
dotnet run --project tools/LibraryLending.DatabaseTool -- verify /backups/library-2026-11-14.db
dotnet run --project tools/LibraryLending.DatabaseTool -- restore /backups/library-2026-11-14.db /data/library-restored.db
```

Use absolute paths and a new destination each time. The utility refuses to overwrite any existing file, checks SQLite integrity, and checks foreign keys. A failed command exits nonzero; retain the source and investigate before using its destination. Protect snapshots like the live member database and define retention/off-host storage in the hosting environment.

To restore service, stop the application, restore into a new file, verify it, and change the connection string to that file. Restart and check readiness, sign-in, loan counts, and reservation links. Keep the previous database until reconciliation is complete. The integration suite restores a snapshot and verifies a committed open loan and reservation records; the utility also has a command-line smoke check in the verification evidence.

## Capacity boundary

Use one application instance with its SQLite file on local persistent storage. The asynchronous write queue reduces in-process SQLite lock contention; database transactions and constraints remain authoritative. Multiple instances, network filesystems, large numbers of persistent Blazor circuits, and external delivery services require separate deployment testing and the changes in [the scaling guide](scalability.md). See [measured service-level results](performance.md) for the tested workload and its limits.
