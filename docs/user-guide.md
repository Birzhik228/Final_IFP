# User guide

Use a librarian account to operate the circulation desk. Use a member account to browse and manage that member's own requests. The development credentials are listed in the README.

## Librarian

### Catalogue and copies

Open Catalogue and search by title, author, or ISBN. Filter by category or availability and use pagination to move through results. Open a title to view its description, physical copy barcodes, statuses, and queue information.

Use the book form to create or edit a title. Supply its authors, ISBN, category, year, and description. A new book can have several physical copies. Add further copies with unique barcodes. Marking a copy Lost uses a controlled circulation action so existing history is preserved.

### Members

Open Members to review contact information, membership status, and borrowing limits. Create or edit a member using the form. A membership record and a login account have different purposes: membership controls lending eligibility; the account controls sign-in.

For a member without an account, select **Create sign-in account** in that row. The application shows the account email and a generated temporary password in a dismissible panel. Share those credentials privately with the member; **Copy credentials** can copy them when browser clipboard permission is available. Ask the member to change the password after signing in. The password is not shown again after the panel is dismissed.

### Borrowing

1. Open the book details.
2. Select the intended member.
3. Choose an Available copy and activate its Borrow action.
4. Confirm that the loan appears on Loans with a due date.

The application rejects inactive membership, overdue loans, an exhausted borrowing limit, and copies that are no longer available. A concurrent change may require refreshing the copy list and trying a different copy.

### Returns

On Loans, locate the open loan and activate Return. Confirm the operation. The final fine is calculated using the loan's captured rate. If someone is waiting for the title, the returned copy becomes Reserved and a pickup deadline is assigned. Otherwise the copy becomes Available.

### Reservation collection

On Reservations, find a Ready reservation and confirm pickup when that member collects the copy. This creates the loan. Pickup fails if the deadline has passed or the member is not currently eligible to borrow. Cancelling a Ready reservation releases the copy to the next waiting member.

### Overdue reports

Open Overdue to view the member, book, due date, overdue days, and accrued fine. Search and choose the sort order. Export CSV to download the report. The exported data is restricted to librarian access and reflects the report's selected filter when supplied.

## Member

### Sign in and change your password

Use the credentials provided by the librarian. Select **Change password** in the header, enter your current password and the new password twice, and submit. The password must meet the displayed Identity policy, including at least ten characters with uppercase/lowercase letters, a digit, and a symbol. The application confirms a successful change; use the new password for future sign-ins.

### Browse and reserve

Open Catalogue and select a title. If all unallocated copies are unavailable, join its reservation queue. Your request is placed at the end of the queue. You cannot create a second active reservation for the same title. If a copy is available, ask the librarian to issue it instead.

### Personal records

Loans shows your own loan history, due dates, and current fines. Reservations shows your own queue positions and pickup deadlines. Cancel a reservation when you no longer need the book. Notifications contains due-soon, overdue, and reservation-ready messages; mark them read after reviewing them.

### Collect a reserved copy

After a Ready notification, collect the book before the displayed deadline. A librarian confirms collection. If you have overdue loans or have reached your borrowing limit, resolve that restriction before collection. An uncollected hold expires and the copy passes to the next person.

## Common messages

| Situation | Action |
| --- | --- |
| Member has overdue loans | Return the overdue items, then retry checkout |
| Borrowing limit reached | Return an open loan or have a librarian review the limit |
| Copy no longer available | Refresh the book and select an available copy |
| Duplicate reservation | Review the existing reservation |
| Pickup deadline passed | Join the queue again if the book is still unavailable |
| Access denied | Sign in with the role allowed to perform the action |
| Connection interrupted | Reconnect or refresh; persistent records remain in SQLite |

The app records fines for demonstration and reporting. It does not collect payments or waive fines through a payment workflow.
