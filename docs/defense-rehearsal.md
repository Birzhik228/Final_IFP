# BookBridge defense rehearsal

**BookBridge — Library Lending & Reservations.** This runbook prepares an 18-minute presentation and demonstration for the required 15–20-minute defense. It does not record a completed human rehearsal. Use it with [the slides](defense-slides.md) and [the ten defense questions](defense-questions.md).

## Fill in before presenting

| Detail | Value |
| --- | --- |
| Presenter 1 | Mubtal Dinmukhamed |
| Presenter 2 | Rukhanov Birzhan |
| Course | Introduction to Functional programming |
| Group, if required | [Group] |
| Defense date | 16 November 2026 |
| Actual contributions | [What each person implemented and verified] |
| Presented source version | Run `git rev-parse --short HEAD` in the repository and record the result in your private presentation notes |
| Temporary public demo | Read the current HTTPS URL from the private access record below |
| Local fallback | `http://localhost:5180` — separate Development instance, database, and credentials |
| Private access record, outside Git | `C:\Users\Adina\Documents\Codex\2026-11-14\rea\outputs\BookBridge-demo-access.txt` |

Keep passwords out of slides, this file, screenshots, terminal recordings, and Git. Retrieve the generated credentials and current connection details privately from the access record once the demo setup has created it. Local development credentials and credentials for the separate public demo are different. A different local database will also have different records after a rehearsal.

The public Production origin at `http://127.0.0.1:5190` is tunnel-only. Its HTTPS redirects and secure cookies make it unsuitable as a direct browser fallback. If the tunnel fails, use the separate Development standby at port 5180; public demo records are not copied there automatically. Keep the temporary public URL out of Git because it can change when the tunnel restarts.

Run the source-version command after the final commit, and verify that the running build corresponds to that version. A commit ID alone does not describe any uncommitted changes.

Mubtal Dinmukhamed and Rukhanov Birzhan should assign slide ranges and the mouse/keyboard role beforehand. Both presenters must be able to explain every layer and the full return-to-reservation transition; the examiner may direct a code question to either person.

## Eighteen-minute schedule

The slide timings total exactly 18 minutes. If questions must fit inside the same 20-minute slot, reserve 18:00–20:00 for them. If interrupted during the talk, shorten the comparisons and future-work discussion; retain the demonstration and the explanation of its transaction.

| Elapsed time | Slide | Purpose |
| --- | --- | --- |
| 00:00–00:30 | 1 | Names, project, and actual contributions |
| 00:30–01:30 | 2 | Concrete library problem and practical value |
| 01:30–02:30 | 3 | Aim, goals, and development plan |
| 02:30–03:45 | 4 | Existing systems and the chosen scope |
| 03:45–04:45 | 5 | Explain the technology choices |
| 04:45–06:00 | 6 | Trace UI → Application → Infrastructure → database |
| 06:00–07:00 | 7 | Book versus copy, loan, reservation, and constraints |
| 07:00–08:00 | 8 | Eligibility, fine calculation, and pure functions |
| 08:00–09:15 | 9 | FIFO assignment, transaction, deadline, and notification |
| 09:15–10:15 | 10 | Mockups, reusable UI, and verification method |
| 10:15–14:15 | 11 | Live demonstration below |
| 14:15–15:30 | 12 | One concurrency problem and its solution |
| 15:30–16:45 | 13 | Actual test evidence and its limits |
| 16:45–18:00 | 14 | Scope, deployment limits, future work, questions |

Opening sentence: “BookBridge helps a small library track physical copies, enforce lending rules, and fairly allocate returned books to waiting readers.”

Transition to the demo: “We will follow one ordinary loan, then watch a return allocate a copy to the first waiting member.”

## Prepare the demonstration

Do this before starting the presentation timer.

1. Start the isolated public demo server and its temporary tunnel. Keep the host computer awake, powered, and connected. Retrieve the current public URL from the private access file. Separately start and check `http://localhost:5180`, using its own credentials and database as the standby. Do not use the production origin on port 5190 as a browser fallback.
2. Use a fresh demo initialized from the standard seed. Verify the preconditions below. Do not reset or overwrite a database while an application is using it. After a full rehearsal, prepare a fresh isolated demo and use its newly generated credentials for the defense.
3. Use **two separate browser profiles or different browsers**, not two ordinary tabs sharing the same sign-in cookies. Sign in to the same demo URL as librarian in profile A and Jamie in profile B. Open **My profile** briefly to verify Jamie's own name, contact information, joining date when recorded, and loan allowance. Keep the credential record off the shared screen.
4. In A, prepare tabs for Catalogue, Loans, Reservations, and Overdue. In B, open Reservations and keep the Clean Code row visible. Arrange the two windows side by side for the live transition.
5. Open the architecture/sequence diagrams and one concurrency test in the editor. Keep the latest local test report and the GitHub Actions result for the presented commit ready. Local tests and remote CI are separate evidence.
6. Test the presentation display, browser zoom, keyboard switching, and CSV download location. Capture a few fallback screenshots from the isolated demo before resetting it; label them with their date and source version.

### Accounts and required starting state

These are fictional seeded library records, not presenter identities. Use the generated passwords for the demo being shown.

| Role or record | Identifier | Required initial state |
| --- | --- | --- |
| Librarian, browser A | `librarian@library.local` | Can access Members, circulation actions, and Overdue |
| Jamie Chen, browser B | `member2@library.local` | Active, no overdue loans, first Waiting reservation for Clean Code |
| Morgan Reed | Member record without a sign-in account | Active, no overdue loans, below the borrowing limit |
| Avery Park | Member record without a sign-in account | No active reservation for Clean Code |
| Pride and Prejudice | `LIB-002-01` | Available; borrow this exact copy |
| Clean Code | `LIB-001-01` | Borrowed by Alex Morgan and overdue |
| Clean Code | `LIB-001-02` | Borrowed by Sam Rivera and overdue |
| Clean Code queue | Jamie Chen, then Robin Ellis | Both Waiting, ordered first and second |

Look up titles and barcodes in the UI; do not depend on database row IDs. If preconditions differ, prepare a fresh isolated demo before presenting rather than improvising against an already changed queue.

The three member sign-in accounts are Jamie (`member2`), Robin (`member7`), and Riley (`member10`), all at `library.local`; the librarian account does not use a member slot. Other member records remain for circulation history and can still be selected by the librarian. Explain that this account cap is separate from the five-book default borrowing limit. Older profiles can legitimately show **Not recorded** for a joining date that was not previously stored.

## Four-minute live script

The times below are relative to the start of slide 11. The people in the Member picker are library members; the presenter remains signed in as librarian when issuing, returning, or collecting a copy.

| Time | Action | Say or verify |
| --- | --- | --- |
| 00:00–00:20 | Show A's librarian navigation and B's Jamie session. | “Staff operate circulation; members see their own records.” Both sessions are signed in before the timer. |
| 00:20–01:05 | In A, search Catalogue for **Pride and Prejudice**, open it, select **Morgan Reed**, and Borrow `LIB-002-01`. Open Loans, search Morgan Reed, and show the new loan. | The copy becomes Borrowed; the new due date is 14 calendar days from the configured library date. Use the displayed date rather than a date written in these notes. |
| 01:05–01:30 | Return that same Pride and Prejudice loan and choose **Confirm return**. Switch Loan status to Returned and search again. | Show its returned history and zero fine for a same-day return. The copy is Available again because nobody was waiting. |
| 01:30–02:00 | In A, open **Clean Code**, select **Avery Park**, and choose **Join reservation queue**. Open Reservations. | Both copies were already borrowed. Jamie remains first, Robin second, and Avery joins third. Creating a new request must not displace earlier requests. |
| 02:00–02:45 | Keep B's Reservations page visible. In A, open Overdue, search **Alex Morgan**, apply the filter, and return **Clean Code**, confirming the action. | Watch Jamie's row in B change from Waiting to **Ready without manually refreshing**. Show barcode `LIB-001-01` and the UTC pickup deadline, 72 hours after assignment. Robin and Avery remain waiting. |
| 02:45–03:15 | In B, open Notifications and show the Clean Code ready message. | The notification is stored in the database. Reloading shows persistence, but only the earlier unrefreshed transition demonstrates live delivery. Do not describe a manual reload as a successful push. |
| 03:15–03:40 | In A, open Reservations and choose **Confirm pickup** for **Jamie Chen / Clean Code**. | Pickup creates Jamie's loan and completes that reservation. A held copy has a dedicated pickup action; ordinary checkout cannot take it for another member. |
| 03:40–04:00 | In A, open Overdue, clear the Alex filter and apply filters, then export CSV. | Other seeded overdue loans remain. Explain accrued versus finalized fines and identify the downloaded report; no payment is collected. |

If the public connection is slow, allow up to 15 seconds to observe the live transition, then follow the recovery plan. If behind schedule, omit opening the downloaded CSV and explain pickup while showing the Ready row; prioritize the borrow, return, queue, and observed member update.

Do not wait 72 hours or change the machine clock to demonstrate expiration. Use `Expiry_at_exact_deadline_passes_copy_to_next_member` in [ReservationIntegrationTests.cs](../tests/LibraryLending.Tests.Integration/ReservationIntegrationTests.cs) to explain the controlled-clock test. Cancellation and expiry both pass the copy to the next waiting active member; pickup additionally checks current borrowing eligibility.

## Code and question rehearsal

Use [the ten model answers](defense-questions.md), but practise answering in your own words. Each presenter should locate the relevant code and describe one invariant, one failure case, and the corresponding test.

| Likely question | Code to open | Point to explain |
| --- | --- | --- |
| Why Book and BookCopy? | [Book](../src/LibraryLending.Domain/Entities/Book.cs), [BookCopy](../src/LibraryLending.Domain/Entities/BookCopy.cs), [Loan](../src/LibraryLending.Domain/Entities/Loan.cs) | An edition may have many separately tracked physical copies; a loan references one copy. |
| Where is functional programming used? | [LendingRules](../src/LibraryLending.Domain/Rules/LendingRules.cs), [Result](../src/LibraryLending.Domain/Results/Result.cs) | Pure calculations and selection rules are deterministic; immutable request/result records separate data from effects. |
| How do fines avoid boundary and rounding errors? | [LendingRules](../src/LibraryLending.Domain/Rules/LendingRules.cs), [rule tests](../tests/LibraryLending.Tests.Unit/LendingRulesTests.cs) | Whole calendar days, integer minor units, zero on the due date, and the loan's captured daily rate. |
| What prevents simultaneous double checkout? | [LibraryService](../src/LibraryLending.Application/LibraryService.cs), [repository](../src/LibraryLending.Infrastructure/Persistence/EfLibraryRepository.cs), [DbContext](../src/LibraryLending.Infrastructure/Persistence/LibraryDbContext.cs) | Recheck inside the write transaction; enforce the unique open-loan constraint; handle conflicting work. |
| Why must the member's limit be checked in that transaction? | [LendingIntegrationTests](../tests/LibraryLending.Tests.Integration/LendingIntegrationTests.cs) | Explain `Two_simultaneous_checkouts_cannot_exceed_one_members_limit`; two independent reads of four loans could otherwise both approve a fifth. |
| How is FIFO deterministic? | [LendingRules](../src/LibraryLending.Domain/Rules/LendingRules.cs), [LibraryService](../src/LibraryLending.Application/LibraryService.cs) | Order by creation time and then ID; persist the assignment, copy state, and notification together. |
| What happens at the pickup deadline? | [ReservationIntegrationTests](../tests/LibraryLending.Tests.Integration/ReservationIntegrationTests.cs) | At the exact deadline the hold is expired; release and reassignment occur atomically. Explain the injected clock rather than waiting in real time. |
| Can one member read or change another member's records? | [CurrentActor](../src/LibraryLending.Web/State/CurrentActor.cs), [LibraryService](../src/LibraryLending.Application/LibraryService.cs), [integration tests](../tests/LibraryLending.Tests.Integration/LendingIntegrationTests.cs) | Identity is resolved on the server; role and ownership checks enforce authorization beyond hidden controls. |
| Why does a missed SignalR push not lose the message? | [LibraryService](../src/LibraryLending.Application/LibraryService.cs), [LibraryPageBase](../src/LibraryLending.Web/State/LibraryPageBase.cs) | Commit the notification before broadcasting; reload persistent data after reconnecting. Do not promise exactly-once network delivery. |
| Why SQLite and what changes for a larger deployment? | [architecture](architecture.md), [repository](../src/LibraryLending.Infrastructure/Persistence/EfLibraryRepository.cs) | Portable course/demo storage; scaling requires provider-specific migrations, concurrency review, coordinated workers, and load testing. A temporary tunnel depends on this computer staying running. |

For the quality slide, read current figures from [testing.md](testing.md) and say which environment produced them. A browser run using a phone-sized viewport is not physical-device testing. Automated accessibility findings are not a complete WCAG audit. A service benchmark is not proof of a particular number of simultaneous public users. Verify the [remote workflow](https://github.com/Birzhik228/Final_IFP/actions) for the presented commit before describing CI as green.

## Recovery if the network or application fails

| Failure | Action during the defense | Accurate explanation |
| --- | --- | --- |
| Public tunnel stops | Open `http://localhost:5180` in both profiles and sign in with the **Development standby's credentials**. Recheck its queue, available copy, and active loans before restarting the scenario if time allows. | “The temporary public connection failed. This is a separate local demonstration with its own database; it does not continue the public session.” |
| Member live update is missed | Give the connection a few seconds, then reload Reservations and Notifications. Show the saved Ready state and message. | “The state and notification were committed. This reload demonstrates recovery; the live push was not observed in this attempt.” |
| Public demo server stops | Use the separate port 5180 standby, or the prepared restart procedure and check the public HTTPS URL. Spend no more than about 30 seconds diagnosing during the talk. | State that the live demonstration was interrupted. Use prepared screenshots and the sequence diagram if neither route is immediately available. |
| Standby data has changed since preparation | Recheck its queue and loan preconditions; restart the scenario only if the remaining time allows. | Explain that this is a separate standby dataset, not continuation of the public session. |
| No working application is available | Show dated screenshots, the return/queue sequence diagram, and relevant automated test evidence. | Clearly label recorded evidence; do not claim those screenshots demonstrate a current live notification. |

Keep a clean demo backup or a tested fresh-demo initialization procedure before the defense. Restore only with its application stopped, following the project's documented backup procedure; never replace a running database as a presentation shortcut. If the public URL changes after restarting the tunnel, update the private access record and the link you share with the examiner.

## Human rehearsal record

Complete this checklist after actually doing each step. No items are pre-checked.

- [ ] Verify the confirmed names, course, and defense date; fill in group if required, actual contributions, and presented commit.
- [ ] Retrieve the current public URL from the private access record; test it and the separate port 5180 standby before the defense.
- [ ] Verify the standby's own credentials and starting data; do not expect the public demo's latest state there.
- [ ] Verify the fresh seed preconditions and both browser sessions using the correct generated credentials.
- [ ] Run the whole presentation aloud with a stopwatch, including browser switches and CSV download.
- [ ] Observe the member's Waiting → Ready change without manual refresh during at least one rehearsal.
- [ ] Rehearse the tunnel-loss fallback and explain the difference between live delivery and persisted recovery.
- [ ] Each presenter explains the return transaction, FIFO ordering, authorization, fine boundaries, and one concurrency test without reading an answer.
- [ ] Each presenter answers a question about code contributed by the other person, if working as a pair.
- [ ] Verify test-report dates and remote CI status for the version being presented.
- [ ] Prepare a fresh isolated demo after rehearsal; recheck credentials and queue state before the real presentation.
- [ ] Close private credentials and unrelated windows before screen sharing.

| Attempt | Date | Total talk/demo time | Issue found | Adjustment for next attempt |
| --- | --- | --- | --- | --- |
| 1 | [Date] | [Minutes:seconds] | [Observed issue] | [Change] |
| 2 | [Date] | [Minutes:seconds] | [Observed issue] | [Change] |

Target an unhurried 18-minute run. If it takes more than 20 minutes, remove repeated explanations and reduce navigation time. If it takes less than 15, deepen the code trace and explain why the transaction and tests protect the demonstrated workflow.
