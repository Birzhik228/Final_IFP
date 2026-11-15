# Borrow request verification

The borrow-request change was verified locally on Windows on **15–16 November 2026, Asia/Qyzylorda**. Browser checks finished before midnight on 15 November; the final backend TRXs were recorded on 16 November. The filenames preserve the date when this verification began. These results concern the feature working tree and do not claim a new remote GitHub Actions result.

## Results

| Suite | Passed | Failed | Skipped | Evidence |
| --- | ---: | ---: | ---: | --- |
| Windows unit | 74 | 0 | 0 | [TRX](borrow-requests/windows-unit.trx) |
| Windows integration | 96 | 0 | 0 | [TRX](borrow-requests/windows-integration.trx) |
| Chromium browser | 20 | 0 | 0 | [TRX](borrow-requests/chromium.trx) |
| Firefox browser | 20 | 0 | 0 | [TRX](borrow-requests/firefox.trx) |
| WebKit browser | 20 | 0 | 0 | [TRX](borrow-requests/webkit.trx) |
| Total executions | **230** | **0** | **0** | [Counters, timestamps and SHA-256 hashes](borrow-requests/test-summary.json) |

There are **190 distinct cases**: 74 unit, 96 integration and 20 browser cases. Repeating the browser cases in three engines produces 230 executions. The integration total includes 20 new request cases, and the browser total includes two new request workflows. No new coverage percentage was collected.

The local SDK was .NET 8.0.428. The final backend test commands used `--no-restore` and `-p:UseAppHost=false`, with real SQLite databases isolated per test. Browser tests used a separately seeded local Kestrel instance and a disposable database, not the user's library database. The browser project built with zero warnings or errors. A separate Release Web build also passed with zero warnings or errors.

## Behavior verified

- A member can request an available title. A pending request leaves copies available and creates no loan.
- Only librarians can accept or reject. Acceptance rechecks availability, active membership, overdue loans, borrowing allowance and existing reservation priority, then creates one loan and records its review atomically.
- Rejection preserves inventory, stores the optional reason and notifies the member. An owner can cancel a pending request and submit another later.
- Members see only their own requests and rejection notes. A mismatched authenticated user/member link cannot submit or read another person's requests.
- Simultaneous duplicate submissions create one pending request. Simultaneous reviews of one request create one loan. Competing requests cannot both take the final copy.
- Failed acceptance leaves the request pending. Existing Waiting and Ready reservations cannot be bypassed. A member must cancel their pending borrowing request before joining the unavailable title's queue.
- The database rejects a duplicate pending member/title request even when application validation is bypassed.
- Browser workflows verify live librarian arrival of new requests, live member decision updates, accepted-loan details, rejection notes, private history and cancellation. Existing circulation, reservation, profile, theme and navigation regressions also passed.

The automated browser run uses Chromium 141.0.7390.37, Firefox 142.0.1 and WebKit 26.0 on Windows. WebKit is engine coverage, not a claim of testing physical Safari/iOS devices.

## Accessibility and interface review

The browser run produced **156 axe-core 4.10.3 page/state scans**, 52 per engine, with **zero reported violations**. The [summary](borrow-requests/accessibility-summary.json) retains per-scan routes, themes, viewport sizes, incomplete-rule IDs and source-file hashes. There were incomplete checks in 116 scans, involving `aria-valid-attr-value`, `color-contrast`, `link-in-text-block` and `th-has-data-cells`. Incomplete checks need human review; zero automated violations does not establish full WCAG conformance.

The `/requests` route was scanned for both roles in light and dark themes. Additional checks covered the light rejection dialog and populated member history at a 390-pixel phone viewport in dark mode. Separate interface inspection verified the accept/reject dialogs, decision history, request cancellation, disabled duplicate-request button, My Shelf navigation, dialog keyboard focus and absence of page-level phone overflow. Tables remain scrollable within their labeled region on narrow screens.

## Migration and local server check

Migration `20261115184806_AddBorrowRequests` adds the request table, foreign keys, status consistency constraint and filtered unique indexes without changing existing circulation rows. A consistent backup of the local SQLite database passed integrity verification before the new Release server started. The migration applied successfully, and `http://localhost:5180/health/ready` returned HTTP 200.

Existing row counts remained unchanged: **30 books, 60 copies, 10 membership records, 19 loans, 2 reservations and 4 Identity users**. Integrity checks passed for both the backup and migrated database. The automated mutation tests used separate test databases.

## Reproduction and scope

From the repository directory, using its .NET SDK:

```powershell
dotnet test tests/LibraryLending.Tests.Unit --no-restore -p:UseAppHost=false --logger "trx;LogFileName=windows-unit.trx" --results-directory artifacts/borrow-requests/backend
dotnet test tests/LibraryLending.Tests.Integration --no-restore -p:UseAppHost=false --logger "trx;LogFileName=windows-integration.trx" --results-directory artifacts/borrow-requests/backend
./tests/LibraryLending.Tests.EndToEnd/Run-BrowserMatrix.ps1 -BaseUrl http://localhost:5190 -ResultsDirectory artifacts/borrow-requests/browser
```

The last command requires an already-running isolated seeded server and installed browsers; see [the testing guide](../testing.md). Linux/macOS execution for this feature, sustained load, physical devices and assistive technology are outside this local verification. Earlier test and coverage records remain historical evidence for their earlier versions.
