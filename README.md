# BookBridge

**Library Lending & Reservations**

BookBridge is a library circulation application built for a C# final project. Librarians maintain books and physical copies, manage members, review borrowing requests, issue and return loans, and review overdue items. Members request available books, join a reservation queue for unavailable books, and see their own profile, requests, loans, and notifications. Solution and namespace identifiers remain `LibraryLending`.

The main feature is an automatic FIFO reservation queue. Returning a copy reserves it for the next waiting member for 72 hours. Expiry or cancellation passes the copy to the next member. Database transactions protect each transition.

## Technology

- C# 12 and .NET 8; Blazor Web App with Interactive Server rendering.
- Bootstrap 5, a custom light/dark theme, and responsive Razor components.
- ASP.NET Core Identity with Librarian and Member roles.
- EF Core and SQLite, migrations, and development seed data.
- SignalR notifications and a background deadline worker.
- xUnit, NSubstitute, FluentAssertions, SQLite integration tests, and Playwright browser tests.

## Run locally

The included launchers use a compatible installed .NET 8 SDK or install SDK 8.0.428 into the project's `.tools` folder. They start the seeded Development instance on port 5180.

Windows PowerShell:

```powershell
./run.ps1
```

macOS/Linux:

```sh
bash run.sh
```

The first launch needs internet access for the SDK, if missing, and NuGet dependencies. For manual setup, install the .NET 8 SDK, which includes the ASP.NET Core runtime. Use the SDK version requested by `global.json`, or a compatible patch permitted by that file. Check it with `dotnet --info`.

From the solution directory:

```sh
dotnet tool restore
dotnet restore
dotnet build
dotnet run --project src/LibraryLending.Web --launch-profile LibraryLending
```

Open the local URL printed by the application. The development profile uses `http://localhost:5180`. The application applies migrations and seeds its development database on first startup. Its seed is repeatable and does not insert duplicate catalogue records on every launch.

The development data includes at least 30 books, multiple physical copies, 10 member records for circulation history, overdue loans, and reservations. Only three member records have sign-in accounts: Jamie Chen, Robin Ellis, and Riley James. Use a separate empty database for automated tests.

Seed records are demonstration data, including synthetic ISBNs and edition years. They are not an authoritative bibliographic catalogue. Clean Code has two overdue borrowed copies and a queue beginning with Jamie Chen; Pride and Prejudice has available copies.

| Development account | Password | Role |
| --- | --- | --- |
| `librarian@library.local` | `LibraryDemo1!` | Librarian |
| `member2@library.local` | `LibraryDemo1!` | Member — Jamie Chen |
| `member7@library.local` | `LibraryDemo1!` | Member — Robin Ellis |
| `member10@library.local` | `LibraryDemo1!` | Member — Riley James |

These credentials are for the local seeded demonstration. Configure real accounts and deployment settings separately before operating a real library.

## Free public demo from GitHub

[Deploy to Render](https://render.com/deploy?repo=https%3A%2F%2Fgithub.com%2FBirzhik228%2FFinal_IFP) using the included Free Blueprint. See [setup, demo accounts and reset behavior](docs/render-demo.md). Render runs the C# server; GitHub Pages cannot run this application by itself. The public demo works while your computer is off, but idle startup takes time and demo data resets when the service restarts, redeploys or sleeps. Use fictional information only.

## Main workflows

1. As a member, open an available book and select **Request to borrow**. Review or cancel the pending request under **My Shelf → Requests**.
2. As a librarian, open **Lending desk → Requests**. **Accept & issue book** creates a loan for an available copy and starts its due-date period. **Reject** closes the request with an optional reason. The member receives a notification of the decision.
3. Librarians can still issue books directly: open **Books**, select a title and member, and borrow an available physical copy. Pending requests do not hold inventory or override an existing reservation queue.
4. Open **Lending desk → Loans & returns** and return the loan. A late return stores the final fine. A waiting reservation receives the returned copy automatically.
5. As a member, open an unavailable book and join its reservation queue. When it becomes Ready, collect it within the pickup window. The librarian confirms collection under **Lending desk → Reservations**. Members see their queue under **My Shelf → Reservations**.
6. Open **Reports** as a librarian to filter and sort overdue loans and download CSV.
7. On **Members**, create a membership and, when a sign-in slot is available, use **Create sign-in account** to issue a generated temporary password. At most **three member login accounts** are allowed; the librarian account is separate. The seeded demo already uses all three slots. Circulation records remain available even when they have no login account.
8. Open **Profile** or your name/avatar in the header to see your own account details. Members see their name, email, phone, joining date when recorded, membership status, borrowing limit, active loans, and remaining allowance. Use **Change password** on the profile page to update your own password.

See [the user guide](docs/user-guide.md) for role-specific instructions and [the architecture guide](docs/architecture.md) for implementation decisions.

## Business defaults

| Setting | Default |
| --- | --- |
| Loan duration | 14 calendar days |
| Member borrowing limit | 5 open loans |
| Member login accounts | Maximum 3, excluding librarians |
| Reservation pickup window | 72 hours |
| Daily overdue fine | 100 minor units, displayed as 1.00 CU |
| Library calendar timezone | UTC |
| Background sweep | Every 60 seconds |

Loan, fine, and calendar options are configured in the `Library` configuration section. Borrowing limits are configurable per member. The three-account maximum is a separate application rule; it does not lower the borrowing limit to three books. A loan keeps a snapshot of its daily fine rate. A fine starts on the calendar day after its due date. No fine accrues on the due date itself.

Only active members can reserve or borrow. Checkout and reservation pickup reject overdue members and members at their borrowing limit. A ready reservation can be collected only for its assigned member. A new reservation cannot be made while an unallocated copy is available, and duplicate active reservations are rejected.

Borrow requests have Pending, Accepted, Rejected and Cancelled states. One member can have only one pending request per title. Submission and acceptance both check borrowing eligibility. Acceptance rechecks availability inside the same transaction that creates the loan, and cannot bypass a waiting reservation queue. If availability changes, the request remains pending until reviewed or cancelled. Members can cancel it before joining the reservation queue. Request history and review details remain stored.

## Project organization

| Project | Responsibility |
| --- | --- |
| `LibraryLending.Domain` | Entities, enums, expected-failure results, and pure rules |
| `LibraryLending.Application` | Use cases and contracts; repository and notification abstractions |
| `LibraryLending.Infrastructure` | SQLite persistence, EF mappings, migrations, Identity, and seeding |
| `LibraryLending.Web` | Pages, components, circuit state, HTTP endpoints, notification hub, and worker |
| `LibraryLending.Tests.Unit` | Deterministic business-rule tests |
| `LibraryLending.Tests.Integration` | Hosted application and real SQLite behavior |
| `LibraryLending.Tests.EndToEnd` | Browser workflows against a running application |
| `LibraryLending.Tests.Performance` | Repeatable service-level load measurements against a larger isolated database |
| `LibraryLending.DatabaseTool` | Consistent SQLite backup, integrity verification, and restore into a new file |
| `LibraryLending.DemoTool` | Isolated sample database with independently generated private demo passwords |

## Testing

```sh
dotnet test tests/LibraryLending.Tests.Unit
dotnet test tests/LibraryLending.Tests.Integration
dotnet test tests/LibraryLending.Tests.Unit --collect:"XPlat Code Coverage"
```

Browser tests require installed Playwright browsers and a running isolated test instance. The commands and complete verification matrix are in [the testing guide](docs/testing.md). Test results are recorded there; a runnable test suite is not itself evidence that all environments have passed.

Current borrow-request verification (15–16 November 2026): **74 unit + 96 integration cases on Windows**, and **20 browser cases on each of Chromium, Firefox and WebKit**, passed with no failures or skips. This is **190 distinct cases / 230 executions**. The browser suite recorded **156 automated accessibility scans with zero reported violations**, retaining incomplete checks for review. See [the borrow-request verification and evidence](docs/verification/borrow-requests-2026-11-15.md). These are local results, not a claim that new remote CI jobs have passed.

Historical baseline verification (14 November 2026): **74 unit and 64 integration tests on both Windows and Linux**, plus **14 browser cases on each of Chromium, Firefox, and WebKit**. All passed. Automated accessibility checks recorded **114 scans with zero reported violations**. Combined runtime coverage was **71.4% lines / 55.4% branches**, including generated EF code. Production HTTPS, bootstrap, readiness, session revocation, and backup/restore checks also passed. See [recorded results and limits](docs/testing.md#recorded-results), [coverage](docs/verification/coverage/index.html), and [measured performance](docs/performance.md). These measurements predate borrow requests; no new coverage measurement is claimed.

Historical profile/account-limit verification (15 November 2026): **74 unit + 75 integration tests on Windows**, **16 Chromium/Edge browser cases**, and **seven checks through the public demo** passed. See [that verification record](docs/verification/profiles-2026-11-15.md) for scope, migration checks, and the updated account rules.

Historical bright-design verification (15 November 2026): **74 unit + 76 integration tests on Windows** and **18 Chromium/Edge browser cases** passed. The redesign includes horizontal role navigation, a clearer catalogue, and persistent dark mode. See [the redesign verification](docs/redesign-verification.md) for behavior, assets, and test limits.

## Documentation and defense

- [Architecture and design decisions](docs/architecture.md)
- [Role-based user guide](docs/user-guide.md)
- [Bright redesign, dark mode, and verification](docs/redesign-verification.md)
- [Testing and coverage](docs/testing.md)
- [HTTP and SignalR endpoint reference](docs/endpoint-reference.md)
- [Scaling and operational limits](docs/scalability.md)
- [Production setup, health checks, and backup/restore](docs/deployment.md)
- [Temporary public demo with a separate database](docs/temporary-demo.md)
- [Measured performance](docs/performance.md)
- [15–20 minute defense slides](docs/defense-slides.md)
- [Timed defense rehearsal and fallback guide](docs/defense-rehearsal.md)
- [Ten defense questions](docs/defense-questions.md)
- [Twenty-criterion rubric checklist](docs/rubric-checklist.md)
- [UI mockups](docs/mockups/wireframes.md)
- [Mermaid class diagram](docs/diagrams/classes.mmd), [ER diagram](docs/diagrams/database.mmd), and [return sequence](docs/diagrams/return-and-reserve.mmd)

## Development notes

Use the injected clock for time-sensitive behavior. Keep database access inside an operation scope and avoid retaining a `DbContext` in a Blazor circuit. Add new rules in the domain/application layer and cover their boundaries before connecting the UI. Preserve reservation and loan history when adding circulation features. Keep secrets out of source control; generated databases and build outputs are excluded by `.gitignore`.

The application records fines but does not process payments. It focuses on one library and does not implement acquisitions, MARC cataloguing, inter-library transfers, or public self-checkout.
