# HTTP and SignalR endpoint reference

The interactive UI calls application services on the server. This project does not require a broad public REST API. Its HTTP endpoints support authentication, exports, and real-time notifications.

| Endpoint | Purpose | Access |
| --- | --- | --- |
| `GET /account/login` | Sign-in form | Anonymous |
| `POST /account/sign-in` | Validate credentials and issue authentication cookie | Anonymous form submission with antiforgery protection |
| `POST /account/logout` | End the authenticated session | Authenticated form submission with antiforgery protection |
| `GET /account/password` | Password-change form | Authenticated |
| `POST /account/change-password` | Change the current user's password and refresh their session | Authenticated form submission with antiforgery protection |
| `GET /exports/overdue.csv` | Download overdue-loan report | Librarian |
| `/hubs/notifications` | SignalR connection for user-specific invalidation/notification events | Authenticated |
| `GET /health` | Lightweight process liveness response | Anonymous |
| `GET /health/ready` | Database connectivity/schema readiness; 503 on failure | Anonymous; generic status only |

The export contains member and book information and must not be available to Member accounts. CSV fields are escaped and spreadsheet formula prefixes are neutralized where applicable. Treat the downloaded report as library data.

The SignalR event `LibraryChanged` tells the browser to reload relevant state. The hub targets server-derived user identifiers. Clients do not choose another member's notification group. Persisted notifications can be read from the Notifications page after reconnecting.

Member account provisioning is an authorized server-side application service invoked by the Members page. A librarian creates the login for an existing membership; the generated temporary password is shown once in that response. It is not a public registration endpoint. The member can change the password after signing in.

Expected circulation failures are represented by `Result<T>` in the application service boundary: for example, an unavailable copy or an overdue borrower. Unhandled server failures use the global error handler. The endpoint reference documents this implementation's HTTP surface; Swagger is unnecessary without an exposed REST API.
