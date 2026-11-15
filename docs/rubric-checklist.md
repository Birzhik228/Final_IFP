# Twenty-criterion rubric checklist

Each criterion is worth 5% in the supplied assignment rubric. This matrix identifies implementation evidence and the verification needed for an honest defense. It is not a predicted grade. Final test outcomes belong in `testing.md`.

| # | Criterion | Evidence to show | Verification or remaining limit |
| --- | --- | --- | --- |
| 1 | Code structure and design | Domain/Application/Infrastructure/Web boundaries and pure rules | Review project references and trace a use case |
| 2 | Component reusability | BookCard, search, member picker, loan table, dialog, pagination, badges, toast, templated table | Show actual use, parameters, callbacks, cascading state |
| 3 | Cross-platform compatibility | .NET/SQLite web application, no OS-specific application logic | Only claim OS runs that were actually performed |
| 4 | State management | Scoped AppState, change notifications, bounded cached lists | Confirm invalidation, user separation, and disposal |
| 5 | UI design | Consistent catalogue, spacing, typography, light/dark theme | Inspect all pages and both themes |
| 6 | User experience | Search, clear state, confirmations, validation feedback | Complete workflows with keyboard and invalid inputs |
| 7 | Responsiveness | Grid/flex layouts and mobile navigation | Check representative desktop/tablet/phone widths |
| 8 | Core functionality | Books/copies, members, loans, returns, overdue, reservations | Run integration and browser flows |
| 9 | Advanced features | SignalR, deadline worker, dashboard, simulated notifications, CSV | Demonstrate each feature with real data changes |
| 10 | Error handling and resilience | Result failures, global errors, atomic transactions, concurrency handling | Competing requests and failed-transition tests |
| 11 | Efficiency | Projection, pagination, short operations, indexes | See measured read/write latency, throughput, CPU, memory, and workload boundaries in performance.md |
| 12 | Scalability | Provider migration and worker/cache notes | Multi-instance deployment is future work |
| 13 | Resource utilization | Bounded state, per-operation contexts, notification deduplication | Measured process CPU/memory; persistent-circuit soak remains unverified |
| 14 | Testing coverage | 14 November baseline: 74 unit + 64 integration cases on Windows and Linux; 14 browser cases on each of three engines. [15 November redesign](redesign-verification.md): 74 unit + 76 integration cases on Windows and 18 Chromium/Edge cases | Baseline combined runtime coverage: 71.4% lines / 55.4% branches; LendingRules 100%. Coverage was not remeasured for the redesign |
| 15 | Quality assurance | Manual matrix, browser workflows, regression checks | 14 November baseline: Windows/Linux and three browser engines passed; 114 automated accessibility scans found zero violations. [15 November redesign](redesign-verification.md): 46 scans, zero violations, and focused manual review of incomplete findings; remote matrix still needs confirmation |
| 16 | Documentation | README, run guide, user guide, architecture, endpoint and test guides | Reproduce setup from a fresh checkout |
| 17 | Innovation | Automatic, explainable FIFO allocation with expiry/reassignment | Describe the fairness policy and edge cases |
| 18 | Creative .NET/FP use | Pure rules, immutable DTOs, Result, LINQ, TimeProvider, templated components | Explain why each technique fits its task |
| 19 | Professionalism | Consistent naming, clear setup, repeatable seed, polished UI | HTTPS/secure cookies, bootstrap, key persistence, readiness, revocation, backup/restore verified; hosted deployment remains untested |
| 20 | Presentation | Timed slide content, comparisons, diagrams, mockups, ten Q&A | Replace names and rehearse a complete demo |

## Scope boundaries

The app records fines but does not take payments. It represents one library and does not implement acquisitions, serials, MARC exchange, inter-library transfers, or a native MAUI client. The SQLite setup is intended for straightforward local operation; larger deployments need the changes described in `scalability.md`.

The live notification transport is supplementary to durable notification records. External email delivery and an outbox-backed mail service are future extensions. Production readiness and high coverage must be demonstrated by measured checks rather than inferred from architecture alone.

Verified evidence is linked from [testing.md](testing.md). Windows and Linux backend checks, Chromium/Firefox/WebKit workflows, keyboard/viewport checks, accessibility scans, and a larger-database service benchmark are recorded. Member reservation filtering runs in SQL and multi-collection book graphs use split queries. macOS, physical devices, full accessibility conformance, long-term circuit capacity, and actual hosted deployment remain outside the verified evidence.
