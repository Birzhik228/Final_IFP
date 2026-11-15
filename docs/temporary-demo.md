# Temporary BookBridge public demo

Use a separate sample database and private demo credentials. The computer must stay awake and connected, and both the app and tunnel must remain running. A temporary tunnel URL is not permanent hosting.

## 1. Initialize an isolated database

Run these PowerShell commands from the repository with the .NET 8 SDK available. Choose a private directory outside the repository; the example uses the current Windows user's local application data.

```powershell
$demoRoot = Join-Path $env:LOCALAPPDATA 'BookBridge-demo'
dotnet run --project tools/LibraryLending.DemoTool -c Release -- initialize "$demoRoot/demo.db" "$demoRoot/credentials.json"
if ($LASTEXITCODE -ne 0) { throw 'Demo initialization failed.' }
```

The tool applies the existing migrations and seeds 30 books, 60 copies, 10 members, 17 loans, and two reservations. It assigns independent cryptographically random passwords to the librarian and all 10 members, then verifies every new password and confirms the documented development password is rejected. Seeding and password rotation are one transaction. This does not enable the web application's Development-only seeder in Production.

`credentials.json` contains an `accounts` array with `email`, `role`, and `password`. Open that local file privately to sign in; do not commit it, publish it, or copy it into logs. No passwords are printed by the initializer. Restrict access to this directory to the account running the demo.

Both output filenames must be new, absolute paths outside the repository. Existing files, SQLite sidecars, and destinations beneath symlinks or junctions are rejected. If initialization fails, do not serve its incomplete database; choose fresh filenames for a retry.

## 2. Publish and start the demo app

```powershell
dotnet publish src/LibraryLending.Web -c Release -o "$demoRoot/app"
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
New-Item -ItemType Directory -Force "$demoRoot/keys" | Out-Null

$env:DOTNET_ENVIRONMENT = 'Production'
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5182'
$env:ConnectionStrings__Library = "Data Source=$demoRoot/demo.db;Default Timeout=5"
$env:DataProtection__KeyPath = "$demoRoot/keys"
$env:Hosting__KnownProxies__0 = '127.0.0.1'
$env:Bootstrap__Enabled = 'false'
$env:Demo__Seed = 'false'
$env:Database__ApplyMigrations = 'false'
$env:BackgroundJobs__Enabled = 'true'

Set-Location "$demoRoot/app"
dotnet LibraryLending.Web.dll
```

These settings apply to this PowerShell process and its children. The dedicated port and database let the ordinary local development app run separately. Keep the database and data-protection keys for the life of this demo; restarting with the same files preserves accounts and sessions. The key directory must remain private.

## 3. Expose the dedicated port

In a second terminal, start a temporary HTTPS tunnel to `http://127.0.0.1:5182` with a provider that supports WebSockets and forwards the original HTTPS scheme. Configure only its actual local proxy address as trusted. Do not expose a Development server or disable secure cookies to work around a tunnel configuration issue.

For example, with Cloudflare's `cloudflared` installed:

```powershell
cloudflared tunnel --url http://127.0.0.1:5182
```

Use the HTTPS URL printed by the tunnel. Local HTTP is redirected to HTTPS in Production; validate authentication through the public HTTPS address. If a local intermediary is needed, retain the app's loopback binding and trust only the loopback proxy, not arbitrary forwarded headers from the internet.

## 4. Check the public demo

The 15 November 2026 public browser run passed all 13 checks, including the second session's live Ready transition. See the [recorded verification and scope](verification/public-demo-2026-11-15.md).

- Open `/health` and `/health/ready` through the public HTTPS address.
- Sign in as the librarian and a member using the private credentials file. Confirm the old development password fails.
- Borrow an available copy, return it, and confirm its availability updates.
- Place a reservation on a title without available copies. Return one of its copies as librarian and confirm the queued reservation becomes ready.
- Keep a second authenticated browser session open to confirm live availability and reservation notifications arrive without refreshing.
- Sign out, sign in again, and confirm the browser has no certificate or mixed-content errors.

The seeded Clean Code copies are borrowed, with member2 and member7 already waiting in that order. Pride and Prejudice initially has available copies. Use only this sample database for public demonstration changes.

## Stop or repeat

Press **Ctrl+C in the tunnel terminal first** to remove public access, then **Ctrl+C in the dedicated demo app terminal**. Leave unrelated development processes alone. Closing these terminals also discards their environment overrides. A later temporary tunnel may receive a different URL.

Restart the published app with the same environment settings and files to resume. For a clean demonstration, run the initializer with a new database and credentials filename, then update the connection string. The initializer deliberately does not reset or overwrite an existing database.
