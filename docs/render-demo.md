# Free public demo on Render

This setup runs the complete C# application, including accounts, borrowing, requests, reservations and live notifications. GitHub stores the code and runs checks; Render runs the server and gives it an HTTPS address ending in `onrender.com`. GitHub Pages cannot run this Blazor Server application.

[![Deploy to Render](https://render.com/images/deploy-to-render-button.svg)](https://render.com/deploy?repo=https%3A%2F%2Fgithub.com%2FBirzhik228%2FFinal_IFP)

## First deployment

1. Sign in to Render, using GitHub if preferred.
2. Open the deploy button above, or create a **Blueprint** from `Birzhik228/Final_IFP`, branch `main`. Authorize Render's GitHub connection for this repository.
3. Review the single `bookbridge-demo` web service. It must show the **Free** compute plan. No database service, paid disk or payment is required by this configuration.
4. Create the Blueprint and wait for the build and health check to pass. Open the exact public URL shown by Render; the final name may include a suffix if the requested name is taken.
5. Check `/health/ready`, sign in with both roles, and test a borrow request, acceptance/rejection, return and reservation notification.

GitHub-linked deployments use `autoDeployTrigger: checksPass`: future pushes to `main` deploy after GitHub checks pass. A service created from only a public repository URL without linking GitHub does **not** support automatic deployments. If using Render's manual Web Service form instead, select Docker, Free, the repository root, and copy the environment settings from `render.yaml`.

## Demo accounts

| Role | Email | Password |
| --- | --- | --- |
| Librarian | `librarian@library.local` | `LibraryDemo1!` |
| Member, Jamie | `member2@library.local` | `LibraryDemo1!` |
| Member, Robin | `member7@library.local` | `LibraryDemo1!` |
| Member, Riley | `member10@library.local` | `LibraryDemo1!` |

These are shared public sample accounts. Visitors can change the same demonstration records and passwords. Use fictional data only. The seeded library has three member sign-in accounts; historical member records are separate from these accounts.

## What is temporary

Render Free stops an idle service after 15 minutes without inbound HTTP or WebSocket traffic. The next visit may take about a minute to start it. Its filesystem is disposable: **SQLite data, requests, account changes and login keys reset when the service restarts, redeploys or spins down.** Startup recreates the sample library. A restart may require users to sign in again. Your computer does not need to stay on.

The Free plan has usage limits. Do not add a paid resource or upgrade the compute plan for this demo. Persistent library hosting needs a separate deployment plan with durable storage and private credentials; use [the production guide](deployment.md) for those requirements.

## Container and HTTPS configuration

The multi-stage Docker build restores locked dependencies and publishes Release output. It runs as the image's unprivileged `app` user on port `10000`. `.dockerignore` excludes local databases, keys, certificates and deployment secrets. The hosted database is created separately at `/tmp/bookbridge/library.db`, with cookie-protection keys under `/tmp/bookbridge/keys`.

`ASPNETCORE_ENVIRONMENT=Demo` explicitly permits the sample seed while retaining non-development secure cookies, HSTS and generic errors. `Database__ApplyMigrations=true` initializes the disposable database at startup. Ordinary `Production` does not seed sample accounts.

Render terminates TLS, redirects public HTTP traffic to HTTPS, and keeps the application's HTTP port inaccessible directly from the public internet. The Blueprint therefore enables the framework's cloud proxy option, `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, so secure cookies and redirects see the original HTTPS scheme. This option accepts forwarded protocol/client headers from upstream peers. **Keep it confined to this isolated Render deployment.** For a directly exposed server or a different host, configure exact trusted proxy addresses instead as described in the production guide. No broad proxy trust is enabled by the Dockerfile itself.

Blazor and notification WebSockets use the same public HTTPS endpoint (`wss`). A redeploy disconnects active sessions; reload after the new instance is ready.

## Local container check

The deployment setup passed nine local hosting integration cases, including Demo seed opt-in, secure sample-account sign-in, HTTPS redirects, database readiness and unchanged Production bootstrap safeguards. The GitHub container workflow builds and runs the actual Linux image. A successful local test is not evidence that the public service has been deployed.

With a running Docker engine, from the repository root:

```sh
docker build -t bookbridge-demo .
docker run --rm -p 127.0.0.1:10000:10000 -e ASPNETCORE_ENVIRONMENT=Development -e Demo__Seed=true -e Database__ApplyMigrations=true -e "ConnectionStrings__Library=Data Source=/tmp/bookbridge/library.db;Default Timeout=5" bookbridge-demo
```

Open `http://localhost:10000`. This local command uses Development over loopback HTTP. Render uses the separate HTTPS configuration in the Blueprint. It does not mount or modify the user's local library database.

## Sources

- [Render web services, isolated port and TLS termination](https://render.com/docs/web-services)
- [Render Free limits and disposable filesystem](https://render.com/docs/free)
- [Render Docker builds](https://render.com/docs/docker)
- [Blueprint fields and deployment triggers](https://render.com/docs/blueprint-spec)
- [Render WebSocket support](https://render.com/docs/websocket)
- [Microsoft proxy configuration and cloud forwarded-header option](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0)
