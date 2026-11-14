# BookBridge defense slides

Target duration: 18 minutes including a short live demonstration. Replace the name placeholders before presenting. Demonstrate with local seeded data and keep the application running before the call begins.

## Slide 1 — Team and project

Time: 30 seconds.

- BookBridge — Library Lending & Reservations.
- Team member 1: [Full name].
- Team member 2, if applicable: [Full name].
- Course: [Course name]. Date: [Defense date].
- Both team members understand the complete borrowing and reservation workflow.

Speaker note: Introduce the project as a circulation tool for a small library. State each member's real contribution; do not claim work someone did not perform.

## Slide 2 — Problem and practical significance

Time: 1 minute.

- A title may have several physical copies with different availability.
- Staff need reliable due dates, overdue reports, and borrower eligibility checks.
- Waiting members need an understandable and fair order of allocation.
- The system keeps circulation records and makes a returned copy available to the next waiting member automatically.

Speaker note: Use a concrete scenario: all copies of a popular title are borrowed, two members are waiting, and one copy is returned.

## Slide 3 — Aim, goals, and plan

Time: 1 minute.

Aim: build an explainable, tested lending workflow with a FIFO reservation queue.

Goals: manage catalogue/copies and members; enforce lending rules; automate reservation assignment and expiry; provide role-based screens and live notifications; document and test the result.

Development plan: rules → persistence → accounts → catalogue → loans → reservations → reports and notifications → testing and defense.

## Slide 4 — Existing systems

Time: 1 minute 15 seconds.

| System | Relevant capability | Lesson for this project |
| --- | --- | --- |
| Koha | Broad integrated library system covering cataloguing, circulation, acquisitions, and reporting | Keep title/copy separation and clear circulation records; keep this project’s scope smaller |
| Evergreen | Circulation and multiple hold levels, including ready-for-pickup handling | Represent reservation states and deadlines explicitly |
| Libib | Lending tools for check-in/out, patrons, due dates, holds, and history | Make common desk workflows direct and readable |
| BookBridge | Focused .NET application with deterministic FIFO assignment | Prioritize explainable rules, atomic transitions, and testability |

Sources reviewed 14 November 2026: [Koha overview](https://koha-community.org/about/), [Evergreen holds](https://docs.evergreen-ils.org/docs/latest/circulation/basic_holds.html), [Libib lending](https://support.libib.com/libib/website/lending.html). The lesson column is our design interpretation, not a claim that those products lack features.

## Slide 5 — Technology choices

Time: 1 minute.

- C# 12/.NET 8 follows the course brief.
- Blazor Interactive Server keeps UI and application logic in the same language.
- SQLite offers a portable local database and repeatable demonstration setup.
- EF Core maps entities and manages schema migrations.
- Identity supplies accounts and roles; SignalR delivers user-specific updates.
- Bootstrap and custom CSS support a responsive light/dark interface.

Speaker note: Compare SQLite's straightforward setup with a server database's better write scalability. Explain why this project does not need a separate mobile client to demonstrate responsive access.

## Slide 6 — Architecture

Time: 1 minute 15 seconds.

- Domain: entities, state vocabulary, pure rules, and results.
- Application: use cases and interfaces.
- Infrastructure: EF Core, repositories, accounts, migrations, and seed data.
- Web: components, authenticated boundaries, state, hub, and worker.
- Tests: rules, database/application integration, and real browser workflows.

Show the dependency diagram in [architecture.md](architecture.md) and the [class diagram](diagrams/classes.mmd).

Speaker note: Explain why Domain does not depend on EF Core or Blazor. Trace one button click from UI to business rule to transaction.

## Slide 7 — Data model

Time: 1 minute.

- Book represents an edition; BookCopy represents a physical item with a barcode.
- BookAuthor supports several authors per title.
- Loan records member, copy, due date, and captured fine rate.
- Reservation keeps queue order, state, copy assignment, and pickup deadline.
- Notifications remain available after a browser disconnects.

Show the [ER diagram](diagrams/database.mmd). Point out unique indexes preventing two open loans for one copy and two active reservations for one member/title.

## Slide 8 — Business rules and functional techniques

Time: 1 minute.

- Default loan: 14 days; default member limit: five open loans.
- Overdue members cannot borrow or collect a reservation.
- Fines use calendar days and integer minor units.
- Pure functions calculate fines, evaluate eligibility, and select the next reservation.
- Immutable records carry requests and responses; `Result<T>` represents expected failure.

Speaker note: Demonstrate that returning on the due date costs zero, while returning one day later costs one daily rate.

## Slide 9 — Reservation algorithm

Time: 1 minute 15 seconds.

1. Return closes the loan and calculates its final fine.
2. Select the oldest waiting active member.
3. Assign the copy, set Ready, and start a 72-hour pickup window.
4. Store the notification and commit the transaction.
5. Notify the member through SignalR.
6. Expiry or cancellation repeats assignment for the next member.

Show the [sequence diagram](diagrams/return-and-reserve.mmd). Explain why a reserved copy cannot be borrowed through ordinary checkout.

## Slide 10 — Methodology and UI

Time: 1 minute.

- Build and verify small complete workflows.
- Use mockups to establish navigation, search, forms, and status hierarchy.
- Reuse book cards, tables, badges, pagination, dialogs, and notifications.
- Keep circuit state scoped and refresh it after successful mutations.
- Test keyboard navigation and mobile layouts.

Show [wireframes](mockups/wireframes.md) beside the running catalogue and detail screen.

## Slide 11 — Live demonstration

Time: 3 minutes.

1. Sign in as librarian and show catalogue search.
2. Issue an available copy to an eligible member; show its due date.
3. Open a fully borrowed title with waiting reservations.
4. Return one of its copies and show the Ready reservation.
5. In a second member session, show the persisted/live notification.
6. Show the overdue report and CSV export.

Preparation: identify the exact seeded book, loan, and member before the defense. Keep one clean database copy available for restoring the demonstration outside the running app. If the network/browser session disconnects, demonstrate the persisted notification after reloading.

## Slide 12 — Challenges and solutions

Time: 1 minute 15 seconds.

| Challenge | Solution |
| --- | --- |
| Two requests borrow the last copy | Transactional checks, constraints, concurrency handling |
| One member borrows two copies with one remaining slot | Check the member aggregate inside the same write transaction |
| Repeated background sweeps create duplicate messages | Notification deduplication keys and state-based processing |
| A missed push notification | Persist before sending; reload on reconnect |
| Time-dependent tests | Inject the clock and test exact boundaries |
| A Blazor circuit outlives a typical HTTP request | Create and dispose database contexts per operation |

## Slide 13 — Quality evidence

Time: 1 minute 15 seconds.

- Unit tests cover eligibility, fines, and queue selection boundaries.
- Integration tests exercise real SQLite constraints, transactions, and authorization.
- Playwright covers borrow, return, reserve, and overdue workflows.
- Coverage is collected with `dotnet test --collect:"XPlat Code Coverage"`.
- Report actual passed/failed counts and measured coverage from [testing.md](testing.md).

Speaker note: Do not invent coverage percentages or imply that untested operating systems have been tested. Open one meaningful concurrency test and explain its assertion.

## Slide 14 — Scope, future growth, and conclusion

Time: 1 minute 15 seconds.

- The completed scope is catalogue management, circulation, reservations, reporting, and notifications.
- A server database, shared cache, and coordinated worker can support larger deployments.
- Future features could include acquisitions, inter-library transfers, and payment handling.
- The central result is a traceable, atomic lending workflow with automatic FIFO allocation.

Finish by inviting questions. Keep [defense-questions.md](defense-questions.md) for rehearsal, not as a substitute for understanding the code.
