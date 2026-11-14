# Performance and platform verification

The performance executable exercises the actual application service, EF Core repository, migrations, and a new file-backed SQLite database. It never connects to the website's database. Every run creates a uniquely named database beside `results.json`.

## Reproduce the measurement

From the repository root, with the SDK in `global.json` installed:

```sh
dotnet run --project tests/LibraryLending.Tests.Performance --configuration Release -- --seconds 20 --concurrency 1,10,25 --output artifacts/performance
```

PowerShell also has `scripts/measure-performance.ps1`; its `-DotNet` parameter accepts an explicit SDK executable. Stop unrelated builds/load tests before measuring. The executable exits nonzero if an operation fails or a consistency check fails. Results are machine dependent; rerun before making a deployment capacity decision.

## Workload and measurement boundaries

- Synthetic initial corpus: 3,000 books, 6,000 physical copies, 1,000 members, 100 authors, 10 categories, 800 open loans (400 overdue), 5,000 returned loans, and 400 waiting reservations. Timestamps are relative to the run date; identifiers and assignments are deterministic.
- Read mix: 70% paginated catalogue searches, 10% staff dashboards, 10% member-specific reservation queues, and 10% filtered overdue-loan searches. Results are checked for expected record count, member isolation, and overdue status.
- Write mix: each worker borrows and returns its own copy for its own eligible member. One reported operation is the complete two-transaction borrow/return pair. All workers write to the same SQLite database.
- Each workload runs for 20 seconds at concurrency 1, 10, and 25. In-flight work finishes before recording elapsed time. The executable warms every operation type and removes completed benchmark loans between scenarios, outside the timed interval, to restore the starting history.
- Successful-operation latency uses nearest-rank p50/p95/p99. Throughput uses successful completions divided by actual elapsed time. Failures are counted separately and grouped by message. CPU covers the benchmark process; one-core percentage may exceed 100%, while machine percentage divides by logical processor count. Working set and private memory are sampled every 200 ms, so the recorded peaks are sampled peaks.
- After load, two eight-way contention checks require exactly one successful borrower for a shared copy and exactly one for a member with a one-loan limit. Database checks reject duplicate open loans, copy/loan status mismatches, and leaked open benchmark loans.

This is a bounded service-level test of one application instance using SQLite. HTTP, authentication, browser rendering, Blazor circuit memory, network latency, and SignalR transport are excluded; notification publishing is a no-op. Memory and CPU describe the console harness; a hosted web process has additional allocations and different garbage-collection settings. It does not establish an internet-facing user limit, multi-instance database capacity, or long-term soak/leak behavior. SQLite still permits only one writer; a singleton async write coordinator queues local write transactions without blocking request threads in native lock waits. Database transactions, constraints, and bounded busy retries remain responsible for correctness across processes.

## Recorded results

Final run on 14 November 2026: Release build, .NET 8.0.34, Windows build 26200 x64, Intel Core i5-12450H (8 physical cores/12 logical processors), approximately 16 GB RAM, SQLite WAL mode. Other project builds and browser tests were stopped during the measurement. The raw [results JSON](verification/performance/results.json) records exact durations, p99/max latency, CPU time, sampled private memory, errors, and consistency counts.

| Workload | Concurrency | Completed | Operations/sec | p50 ms | p95 ms | Errors |
|---|---:|---:|---:|---:|---:|---:|
| Read mix | 1 | 1,171 | 58.5 | 16.8 | 33.3 | 0 |
| Borrow + return pair | 1 | 1,476 | 73.8 | 12.5 | 19.8 | 0 |
| Read mix | 10 | 2,536 | 126.6 | 93.2 | 140.7 | 0 |
| Borrow + return pair | 10 | 1,550 | 77.2 | 125.4 | 159.5 | 0 |
| Read mix | 25 | 1,674 | 83.1 | 200.3 | 507.3 | 0 |
| Borrow + return pair | 25 | 1,508 | 74.6 | 328.1 | 380.8 | 0 |

| Workload | Concurrency | Process CPU, machine % | Working set before MiB | Sampled peak MiB | After MiB |
|---|---:|---:|---:|---:|---:|
| Read mix | 1 | 8.6 | 154.5 | 154.5 | 102.5 |
| Borrow + return pair | 1 | 5.1 | 102.5 | 111.3 | 104.1 |
| Read mix | 10 | 25.3 | 104.9 | 125.5 | 123.1 |
| Borrow + return pair | 10 | 5.2 | 123.1 | 123.7 | 123.4 |
| Read mix | 25 | 60.8 | 123.4 | 141.3 | 137.3 |
| Borrow + return pair | 25 | 5.0 | 137.3 | 138.0 | 137.4 |

All **9,915 operations/pairs** completed without errors. Both contention checks had exactly one winner; duplicate open loans, copy/loan mismatches, and leaked open benchmark loans were all zero. Higher read concurrency did not continuously increase throughput: the 25-worker run consumed more CPU and completed fewer reads per second than the 10-worker run. More concurrent requests should therefore not be treated as free capacity. A preliminary five-second diagnostic exposed multi-second writer waits and motivated the write coordinator; it used different warmup/background-load conditions, so it is not a controlled before/after speedup comparison.

## Linux verification

On 14 November 2026, a separate Linux source snapshot built successfully with zero warnings/errors and passed **74 unit tests and 64 integration tests**, including production-readiness, authentication, migration/backup, concurrent writer, cancellation, and failure-path checks. Environment: WSL2 x64, Ubuntu 26.04.1, Linux kernel 6.6.87.2, .NET SDK 8.0.428/runtime 8.0.34.

After the final UI changes, the Linux solution rebuilt with zero warnings/errors. An actual Kestrel listener on port 5185 returned healthy process and database-readiness responses and the BookBridge login page/subtitle. The isolated server shut down after the check; see the [smoke log](verification/linux/smoke.log). The strengthened online-backup test also passed independently with a verified, populated WAL file before snapshot creation; see the [targeted test result](verification/test-results/linux-wal-backup.trx).

The existing WSL distribution had no .NET SDK or ICU library. For this check, both were extracted to an isolated scratch directory; ICU 78 was selected using `DOTNET_ICU_VERSION_OVERRIDE=78` and `LD_LIBRARY_PATH`. No system package installation was performed. NuGet restore used the existing package cache, and all Linux `bin`/`obj` output was separate from Windows output. This demonstrates execution on this Linux environment; it is not a claim that every Linux distribution is supported without runtime prerequisites.

On a normally provisioned Linux checkout, run:

```sh
bash scripts/verify-linux.sh
```

This restores/builds, runs unit and integration suites, and starts a temporary seeded Kestrel instance for health, database readiness, and login-page checks. `BOOKBRIDGE_SMOKE_PORT` overrides the default port 5185. Logs and test results are under `artifacts/platform/linux`; each smoke run uses a separate database and stops its own server afterward.

`.github/workflows/verify.yml` configures unit/integration checks for Ubuntu, Windows, and macOS, plus Chromium, Firefox, and WebKit workflow/accessibility checks on Ubuntu. This configuration has not been executed on GitHub Actions as part of the local verification. No local macOS execution is claimed.
