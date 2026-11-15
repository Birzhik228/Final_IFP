# Testing and verification

Verification is split into pure rules, application/database integration, and browser workflows. Run against an isolated test database so circulation tests cannot modify a user's working library data.

## Unit tests

```sh
dotnet test tests/LibraryLending.Tests.Unit
```

Cover overdue-day boundaries, zero/positive fine calculations, inactive memberships, exhausted borrowing limits, overdue borrowers, FIFO ordering, and empty queues. Use explicit dates and the injected clock; do not depend on the test machine's current date.

## Integration tests

```sh
dotnet test tests/LibraryLending.Tests.Integration
```

Use WebApplicationFactory with an isolated SQLite database and the real EF mappings/migrations. Important cases include two borrowers competing for one copy, one member competing for their final loan slot, duplicate reservations, repeated returns, atomic return-to-reservation assignment, expiry, cancellation, authorization, and notification deduplication.

`BorrowRequestTests` adds 20 cases for available-book requests: pending requests do not hold inventory; acceptance creates an audited loan; rejection and cancellation preserve inventory; member reads and commands remain private; duplicate submissions and concurrent reviews cannot duplicate requests or loans. Tests also recheck eligibility and availability at acceptance, preserve the existing reservation queue's priority, and exercise the database's filtered unique index directly.

SQLite in-memory databases can be suitable for ordinary repository checks if their connection remains open. Concurrency tests should use separate connections to a temporary file database so they exercise actual locking behavior.

## End-to-end tests

The browser suite uses Microsoft.Playwright.Xunit and a real Kestrel instance. Start the app on an isolated test database using its Development profile. Set `ConnectionStrings__Library` to a SQLite connection string containing a disposable file path and keep the normal development database separate. Set `BackgroundJobs__Enabled=false` for a repeatable browser run; background processing has separate controlled-clock integration tests.

Build the browser test project and install all three engines:

```sh
dotnet build tests/LibraryLending.Tests.EndToEnd
pwsh tests/LibraryLending.Tests.EndToEnd/bin/Debug/net8.0/playwright.ps1 install chromium firefox webkit
```

On Linux CI, install each browser's system dependencies with Playwright's `install --with-deps` option. The CI browser matrix does this for Chromium, Firefox, and WebKit.

In one terminal, run the isolated instance:

```sh
dotnet run --project src/LibraryLending.Web --launch-profile LibraryLending
```

In another terminal:

```sh
dotnet test tests/LibraryLending.Tests.EndToEnd -- Playwright.BrowserName=chromium
dotnet test tests/LibraryLending.Tests.EndToEnd -- Playwright.BrowserName=firefox
dotnet test tests/LibraryLending.Tests.EndToEnd -- Playwright.BrowserName=webkit
```

The default base URL is `http://localhost:5180`. Override it with the environment variable `LIBRARY_BASE_URL`. The tests sign in using the explicitly seeded development accounts and create uniquely named records for mutation workflows. Do not run them against a deployed real library.

The 20 browser cases cover borrowing/returning, editing an empty optional cover URL, member reservation and live allocation, catalogue search/filter controls, overdue filtering/CSV, phone layout, member account provisioning/password change, profile isolation, role/theme accessibility scans, keyboard dialogs/navigation, tablet layout, and short landscape navigation. Borrow-request workflows verify live librarian review, accepted loans, rejected notes, member privacy and cancellation. Theme checks cover the operating-system default and saved choices across reloads, page navigation, and account forms for both roles. Populated unread/read notifications, the rejection dialog and request history at phone width also receive accessibility scans.

Set `LIBRARY_SCREENSHOTS` for screenshots and `LIBRARY_A11Y_RESULTS` for axe-core JSON. The bundled, pinned axe-core 4.10.3 uses WCAG 2.0 A/AA and WCAG 2.1 AA rule tags. Reports retain both violations and incomplete checks that need human review. Automated scans do not prove full accessibility conformance.

PowerShell users can run all engines and save their evidence together:

```powershell
./tests/LibraryLending.Tests.EndToEnd/Run-BrowserMatrix.ps1 -BaseUrl http://localhost:5183 -InstallBrowsers
```

## Coverage

Collect coverage for the unit and integration suites:

```sh
dotnet test tests/LibraryLending.Tests.Unit --collect:"XPlat Code Coverage" --results-directory artifacts/coverage/unit
dotnet test tests/LibraryLending.Tests.Integration --collect:"XPlat Code Coverage" --results-directory artifacts/coverage/integration
```

The local tool manifest includes ReportGenerator and dotnet-coverage. Browser tests run in a separate process from the server, so collect server coverage explicitly if UI execution should contribute to the report. Build Web, set Development/demo-seed and an isolated database in the server environment, then launch:

```sh
dotnet publish src/LibraryLending.Web -c Debug -o artifacts/coverage-host
dotnet coverage collect -id library-browser -o artifacts/coverage/browser/coverage.cobertura.xml -f cobertura --include-files "artifacts/coverage-host/LibraryLending*.dll" dotnet artifacts/coverage-host/LibraryLending.Web.dll --contentRoot artifacts/coverage-host --urls http://localhost:5181
```

Run the browser suite in another terminal with `LIBRARY_BASE_URL=http://localhost:5181`. Stop collection after the tests with `dotnet coverage shutdown library-browser`. Merge the collected files into an HTML report:

```sh
dotnet reportgenerator -reports:"artifacts/coverage/**/coverage.cobertura.xml" -targetdir:"artifacts/coverage/report" -reporttypes:"Html;TextSummary" -assemblyfilters:"+LibraryLending.*;-LibraryLending.Tests.*"
```

Review branch coverage for rules and application decisions, not only the overall line percentage. Report measured values after collection. Coverage shows what executed; assertions and boundary cases establish whether behavior was checked.

## Manual verification matrix

| Area | Check |
| --- | --- |
| Desktop | Catalogue, detail, borrow requests, loans, reservations, members, dashboard, reports |
| Tablet/phone | Navigation, stacked forms, usable tables, no page-level horizontal overflow |
| Keyboard | Tab order, visible focus, button activation, dialog close/cancel behavior |
| Roles | Librarian actions unavailable to Member; member can only access own records |
| Validation | Empty title, invalid ISBN/year, duplicate barcode, invalid member limit |
| Time | Due-date boundary, pickup deadline boundary, changed fine-rate configuration |
| Notifications | Two sessions, new requests and review decisions, successful update after return, recovery after reconnect |
| Error recovery | Concurrent borrow, repeated return, database contention, disconnected circuit |
| Themes | Text/status readability in light and dark modes |
| Browsers | Chromium, Firefox, WebKit; run the complete browser matrix |
| Platforms | Current feature verified on Windows; earlier Linux results below are historical; run current Linux/macOS jobs in CI |

## Recorded results

The latest [borrow-request verification](verification/borrow-requests-2026-11-15.md) covers the working tree tested on **15–16 November 2026, Asia/Qyzylorda**. It passed **74 unit and 96 integration cases on Windows**, plus **20 browser cases on each of Chromium, Firefox and WebKit**. This is **190 distinct cases and 230 successful executions**, with zero failures or skips. Browser repetitions are not extra distinct tests. The retained [TRXs and accessibility summary](verification/borrow-requests/) support these local results; they do not establish a new GitHub Actions result.

The browser run recorded **156 axe-core page/state scans with zero reported violations**. Incomplete checks remain recorded for human review. The new `/requests` route is included for both roles in both themes, with additional rejection-dialog and dark phone-history checks. No fresh coverage percentage is claimed for the borrow-request change.

### Historical results before borrow requests

The [15 November redesign verification](redesign-verification.md) records the earlier 74 unit, 76 integration and 18 Chromium/Edge cases. Its counts and the results below predate borrow requests and must not be used as evidence for the new feature.

Verified on 14 November 2026 using .NET SDK 8.0.428/runtime 8.0.34. Windows Release solution build and locked dependency restore passed with zero compiler warnings/errors. Linux verification used a separate native build on WSL2 Ubuntu 26.04.1 x64; see [platform details](performance.md#linux-verification).

| Suite | Passed | Failed | Skipped | Evidence |
| --- | --- | --- | --- | --- |
| Windows unit | 74 | 0 | 0 | [TRX](verification/test-results/windows-unit.trx) |
| Windows integration | 64 | 0 | 0 | [TRX](verification/test-results/windows-integration.trx) |
| Linux unit | 74 | 0 | 0 | [TRX](verification/test-results/linux-unit.trx) |
| Linux integration | 64 | 0 | 0 | [TRX](verification/test-results/linux-integration.trx) |
| Windows chromium browser | 14 | 0 | 0 | [TRX](verification/test-results/chromium.trx) |
| Windows firefox browser | 14 | 0 | 0 | [TRX](verification/test-results/firefox.trx) |
| Windows webkit browser | 14 | 0 | 0 | [TRX](verification/test-results/webkit.trx) |

That is **138 backend cases on each operating system**, plus **42 browser executions** (14 cases on each engine). The production-readiness cases are included in the integration total. Browser engines: chromium 141.0.7390.37, firefox 142.0.1, webkit 26.0. WebKit testing is engine coverage on Windows, not a test on a real Safari/macOS/iOS device.

The browser suite performed **114 axe-core page/state scans with zero reported violations** across both roles, both themes, and populated unread/read notifications. JSON retains incomplete checks for manual review. Keyboard checks exercise dialog focus trapping/restoration, mobile-menu focus and Escape, and short landscape navigation; viewport checks cover phone, tablet, landscape, and desktop widths. These checks and screenshot inspection do not certify full WCAG conformance or replace screen-reader and physical-device testing. See [accessibility evidence](verification/accessibility/summary.json) and [current screenshots](verification/screenshots/chromium/dashboard-desktop-dark.png).

The real production-mode Kestrel smoke passed an HTTPS handshake, HTTP redirect, sign-in, Secure/HttpOnly cookies, certificate-encrypted persistent keys, online command-line backup, restore, and integrity verification. It used a temporary self-signed certificate without changing system trust. See [production results](verification/production/results.json) and [deployment instructions](deployment.md). Linux Kestrel health/readiness/login checks are recorded in [the Linux smoke log](verification/linux/smoke.log).

## Historical measured coverage

The **14 November baseline** [combined coverage report](verification/coverage/index.html) merges its Windows unit/integration runs with instrumented server execution during the three-browser suite. It covers the four runtime application assemblies; test/performance assemblies and the command-line entry point are outside this metric. That baseline measured **71.4% of lines and 55.4% of branches**, including generated EF migrations/model snapshots. Coverage was not recollected after borrow requests were added.

| Runtime assembly | Line coverage |
| --- | --- |
| Application | 93.4% |
| Domain | 90.1% |
| Infrastructure | 65.1% |
| Web | 84.6% |
| LendingRules class | 100% |

The earlier run measured 72.3% lines and 67.0% branches. New regressions exercise rollback, direct/wrapped SQLite busy retries, exhausted retries, failed/cancelled live delivery after commit, writer-queue cancellation/release, SQL FIFO/member isolation, malformed input, failed deadline processing, production bootstrap, session revocation, database readiness, and snapshot restore. Coverage still has gaps; generated snapshots and rare error branches are not made safe simply by a percentage. Coverlet and dotnet-coverage may represent generic/generated methods differently. Browser server coverage is not JavaScript statement coverage.

## Performance and remaining scope

See [performance.md](performance.md) for the larger-database workload, throughput/latency, CPU/memory measurements, concurrency invariants, and reproducible commands. Measurements use one service process and SQLite, excluding HTTP/browser/circuit/network costs. No internet-facing user capacity or long-term soak result is claimed.

The GitHub workflow configures three operating systems and three browser engines. The borrow-request results above are local Windows evidence; a new remote CI run and current-feature Linux/macOS execution are not claimed. Physical phones/tablets, assistive-technology review, long-running circuit-load tests and production hosting require their own verification.
