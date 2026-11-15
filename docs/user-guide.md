# User guide

Use a librarian account to operate the circulation desk. Use a member account to browse and manage that member's own requests. The development credentials are listed in the README.

## Your profile

Both members and librarians can open their profile through the name/avatar in the header; members also have a **Profile** navigation link. Each signed-in person sees only their own profile. Members see their full name, email, phone, membership ID and status, joining date, borrowing limit, active loan count, and how many more books they can borrow. A remaining allowance does not override an overdue-loan or inactive-membership restriction.

Joining dates are recorded when new membership records are created. Older records without a known date show **Not recorded**; the application does not invent a past joining date. Librarians see their account name, email, and role without member-only lending details. Use **Change password** to update your own password; ask the librarian to correct membership/contact details.

## Librarian

### Catalogue and copies

Open **Books** and search by title, author, or ISBN. Filter by category or availability, sort by title or recently added, and use pagination to move through results. Open a title to view its description, physical copy barcodes, statuses, and queue information.

Use the book form to create or edit a title. Supply its authors, ISBN, category, year, and description. A new book can have several physical copies. Add further copies with unique barcodes. Marking a copy Lost uses a controlled circulation action so existing history is preserved.

### Members

Open Members to review contact information, membership status, and borrowing limits. Create or edit a member using the form. A membership record and a login account have different purposes: membership controls lending eligibility; the account controls sign-in.

The library allows a maximum of **three member sign-in accounts**, excluding the librarian account. The Members page shows how many slots are used. This is separate from each member's borrowing limit, which defaults to five open loans. Member records and their loan history may exist without a sign-in account.

For a member without an account, select **Create sign-in account** in that row. If all three slots are used, the application explains the limit and creates no account. Otherwise it shows the account email and a generated temporary password in a dismissible panel. Share those credentials privately with the member; **Copy credentials** can copy them when browser clipboard permission is available. Ask the member to change the password after signing in. The password is not shown again after the panel is dismissed.

The seeded demo's three member logins belong to Jamie Chen (`member2@library.local`), Robin Ellis (`member7@library.local`), and Riley James (`member10@library.local`). The other seeded members remain circulation records for the library's history.

### Borrowing

1. Open the book details.
2. Select the intended member.
3. Choose an Available copy and activate its Borrow action.
4. Confirm that the loan appears under **Lending desk → Loans & returns** with a due date.

The application rejects inactive membership, overdue loans, an exhausted borrowing limit, and copies that are no longer available. A concurrent change may require refreshing the copy list and trying a different copy.

### Returns

Under **Lending desk → Loans & returns**, locate the open loan and activate Return. Confirm the operation. The final fine is calculated using the loan's captured rate. If someone is waiting for the title, the returned copy becomes Reserved and a pickup deadline is assigned. Otherwise the copy becomes Available.

### Reservation collection

Under **Lending desk → Reservations**, find a Ready reservation and confirm pickup when that member collects the copy. This creates the loan. Pickup fails if the deadline has passed or the member is not currently eligible to borrow. Cancelling a Ready reservation releases the copy to the next waiting member.

### Overdue reports

Open **Reports** to view the overdue member, book, due date, overdue days, and accrued fine. Search and choose the sort order. Export CSV to download the report. The exported data is restricted to librarian access and reflects the report's selected filter when supplied.

## Member

### Sign in and change your password

Use the credentials provided by the librarian. Open **Profile**, select **Change password**, enter your current password and the new password twice, and submit. The password must meet the displayed Identity policy, including at least ten characters with uppercase/lowercase letters, a digit, and a symbol. The application confirms a successful change; use the new password for future sign-ins.

### Browse and reserve

Open **Books** and select a title. If all unallocated copies are unavailable, follow **Join waiting list** to its reservation section and join the queue. Your request is placed at the end of the queue. You cannot create a second active reservation for the same title. If a copy is available, ask the librarian to issue it instead.

### Personal records

**My Shelf → My loans** shows your own loan history, due dates, and current fines. **My Shelf → Reservations** shows your own queue positions and pickup deadlines. Cancel a reservation when you no longer need the book. The header notification bell opens due-soon, overdue, and reservation-ready messages; mark them read after reviewing them.

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
