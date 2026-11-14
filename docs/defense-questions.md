# Ten defense questions

1. **Why separate Book from BookCopy?** A book describes an edition; copies are physical objects with unique barcodes and independent availability. A loan must reference the actual copy.

2. **How do you prevent two people from borrowing the same copy?** Check availability and create the loan in one write transaction. A database uniqueness constraint protects open loans, and concurrency handling converts conflicting work into a retry or readable failure.

3. **Why is checking the member limit outside the transaction unsafe?** Two requests could both read four active loans and each create a fifth. Re-evaluating the aggregate inside the serialized write transaction prevents that race.

4. **How is FIFO guaranteed?** Select Waiting reservations ordered by creation time and then ID. Persist the assignment atomically with the copy state. Ready reservations use a separate pickup path so another borrower cannot take the held copy.

5. **What happens when a pickup deadline expires?** Mark that reservation Expired, release its copy, and allocate it to the next waiting active member in one transaction. If there is no waiting member, the copy becomes Available.

6. **Where do functional-programming ideas appear?** Fine and overdue calculations, borrowing eligibility, and queue selection are pure functions. Records carry immutable requests/results, LINQ expresses selection, and `Result<T>` handles expected failures without exception-driven control flow.

7. **Why use a fresh DbContext per operation in Blazor?** A Blazor circuit can live a long time and several components can do work concurrently. DbContext is not thread-safe; an operation scope keeps tracking and transactions bounded.

8. **How do you stop a member reading another member's loans?** The authenticated user maps to a member on the server. Member queries use that identity, and application operations check ownership. Hiding a UI button alone is insufficient.

9. **What if SignalR fails after a reservation becomes ready?** The notification was already saved with the reservation transaction. The member can reload it from the database; the push improves immediacy but is not the only copy of the message.

10. **How would you scale beyond SQLite?** Introduce a server EF Core provider with provider-specific migrations, review locking/concurrency behavior, keep operation boundaries, add bounded caching, coordinate background jobs, and use a persistent message/outbox strategy for stronger delivery guarantees. Test these changes under concurrent load rather than claiming a connection-string change is sufficient.
