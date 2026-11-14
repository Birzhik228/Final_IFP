# UI mockups

These wireframes describe the intended information hierarchy. The application uses the same workflow on desktop and small screens; on a phone, navigation and cards stack vertically and tables allow horizontal scrolling.

## Catalogue

```text
+-----------------+-------------------------------------------------+
| BookBridge      | Catalogue                         Theme | Account|
|                 | Find your next book                             |
| Dashboard       | [Search title / author / ISBN] [Category]       |
| Catalogue       | [ ] Available only                 [Search]    |
| Loans           |                                                 |
| Reservations    | +--------------+ +--------------+ +------------+|
| Overdue*        | | Book cover   | | Book cover   | | Book cover ||
| Members*        | | Title        | | Title        | | Title      ||
| Notifications   | | Author       | | Author       | | Author     ||
|                 | | Availability | | Availability | | Available  ||
|                 | +--------------+ +--------------+ +------------+|
|                 |                      Previous | 1 of 4 | Next  |
+-----------------+-------------------------------------------------+
* Librarian access
```

## Book detail and circulation

```text
Catalogue > Book title

[Cover] Title                       [Edit book: librarian]
        Author(s) / category / year
        Description

Member [Select member: librarian]    [Join reservation queue]

Copy barcode          Status        Action
COPY-001              Available     Borrow
COPY-002              Borrowed      —
COPY-003              Reserved      —

Queue information and eligibility feedback
```

## Dashboard

```text
Good day, librarian
[Titles] [Available copies] [Active loans] [Overdue]

Most borrowed titles                    Circulation shortcuts
Title A  █████████████  12               Browse catalogue
Title B  █████████       8               Review overdue loans
Title C  █████           4               Manage reservations
```

## Mobile

```text
+--------------------------+
| Menu  BookBridge    Theme |
| Catalogue                |
| [Search books          ] |
| [Category              ] |
| [ ] Available  [Search]  |
| +----------------------+ |
| | Cover / Title        | |
| | Author / Status      | |
| +----------------------+ |
| Previous   1 of 4   Next |
+--------------------------+
```

Form labels stay visible above fields. Statuses use readable text alongside color. Confirmation dialogs state the action and expose a clear cancel button. Empty lists explain that no matching records were found. Errors appear near the affected action, and toast notifications report successful changes without replacing the page content.
