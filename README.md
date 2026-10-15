# SignalDesk

Turn scattered IT reports into a coordinated incident response.

## Current scope — lesson 1

React + TypeScript frontend and ASP.NET Core 10 API. Submit reports, list reports,
and change status. Version numbers reject stale updates with HTTP 409.
This is a learning starter, not the completed incident platform.

**Local development only:** data lives in memory and is lost on API restart.
There are no accounts, organization permissions, incidents, SignalR, or notifications yet.
The API refuses to start outside Development. Use fictional reports only.

## Install on Windows

Install VS Code, Git, Node.js 24 LTS, and the .NET 10 **SDK** (not only the runtime).
Install the C# Dev Kit extension in VS Code. Docker Desktop and PostgreSQL are for lesson 2.
Verify in a new PowerShell window:

```powershell
git --version
node --version
npm --version
dotnet --version
```

Extract this project, open its SignalDesk folder in VS Code, then open two terminals.

Terminal 1, from the project root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project backend/SignalDesk.Api --urls http://localhost:5080
```

Terminal 2, from the project root:

```powershell
cd frontend
npm install
npm run dev
```

Open http://localhost:5173. The API health endpoint is http://localhost:5080/health.
If PowerShell blocks npm.ps1, use `npm.cmd install` and `npm.cmd run dev`.

## First exercise

1. Submit a fictional Wi-Fi report for STEM, floor 3.
2. Submit another report for the same service and location.
3. Change one status to Investigating, then Resolved.
4. Refresh the browser: reports remain while the API is running.
5. Restart the API: reports disappear. This demonstrates why lesson 2 needs a database.
6. Open two browser tabs; refresh both so they have the same report version.
   Change a status in tab A, then change it in stale tab B without refreshing.
   Tab B should show a conflict, fetch fresh data, and let you retry intentionally.

## Read the code in this order

1. `backend/SignalDesk.Api/Program.cs`: records define the request/response shapes.
   GET reads reports, POST validates and creates one, PATCH updates an existing report.
   The lock makes version checking and updating one atomic operation in this process.
2. `frontend/src/main.tsx`: the Report type describes API data; request sends HTTP calls;
   useState stores UI state; submit and status invoke the API; JSX renders the screen.
3. `frontend/src/style.css`: layout, controls, and responsive styles.
4. `.github/workflows/ci.yml`: checks a clean build after a push or pull request.

Try adding a read-only count of unresolved reports. Explain where its value comes from
before coding it. Then add a required Room field to both API validation and the form.

## Build checks

```powershell
dotnet build backend/SignalDesk.Api
cd frontend
npm run build
```

CI compiles both projects. Integration and browser tests are a later milestone;
a successful compile is not proof of correct behavior.

## Put it on GitHub

Create an empty repository named `signaldesk` on GitHub (do not initialize a README).
Run these from the extracted SignalDesk folder, replacing YOUR-USERNAME:

```powershell
git init -b main
git add .
git commit -m "Start SignalDesk report submission and status workflow"
git remote add origin https://github.com/YOUR-USERNAME/signaldesk.git
git push -u origin main
```

Commit the generated frontend/package-lock.json so dependency versions are reproducible.
Authenticate through Git's browser prompt. Never put access tokens in source files.

## Implementation roadmap

Complete one milestone and its acceptance check before moving on.

| Milestone | Build | Done when |
| --- | --- | --- |
| 1: Local vertical slice | Report form, API, validation, status, version conflicts | Two tabs reproduce a stale-write conflict |
| 2: Durable data | PostgreSQL, EF Core, migrations, database-enforced concurrency | Reports survive restart; concurrent writes cannot overwrite silently |
| 3: Accounts and scope | ASP.NET Identity, organizations, Reporter/Technician/Admin roles | Automated tests reject another organization's IDs and unauthorized status changes |
| 4: Incident workflow | Incidents, report linking, assignment, transition rules, timeline | Two reports link to one incident without deleting either report |
| 5: Grouping suggestions | Same service + location + recent time window; human approval | Technician can approve or reject a suggestion with an audit trail |
| 6: Live collaboration | SignalR, authenticated organization groups, reconnect/resync, presence | Two technicians see changes live; reconnect fetches missed state |
| 7: Reliable background work | Transactional outbox, Hangfire, retry/backoff, delivery records | Simulated provider failure recovers without duplicate logical notifications |
| 8: Analytics and release | Acknowledgment/resolution timestamps, charts, xUnit, Playwright, telemetry, deployment | Deployed demo passes end-to-end tests and documents measured benchmarks |

Add Tailwind and TanStack Query as the UI grows. Add Docker with PostgreSQL in milestone 2.
Avoid introducing every library before there is a workflow that needs it.

## Database design to implement next

- Organization: id, name.
- ApplicationUser: Identity account plus organization membership.
- Report: organizationId, reporterId, serviceId, location, description, createdAt,
  optional incidentId, concurrency token.
- Incident: organizationId, serviceId, title, severity, status, assignedTechnicianId,
  createdAt, acknowledgedAt, resolvedAt, concurrency token.
- IncidentUpdate: incidentId, authorId, message, visibility, createdAt.
- OutboxMessage and NotificationDelivery: durable work and deduplication keys.

Reports are observations; incidents represent coordinated investigations.
Many reports may belong to one incident. Linking reports preserves their provenance.
Check organization membership on every query, command, and SignalR group join.
Database transactions and conditional updates replace the single-process lock.

## Deployment plan

GitHub stores the full source and runs CI. GitHub Pages hosts static frontend assets only;
it cannot execute the ASP.NET API, host PostgreSQL, or run Hangfire.

After accounts and persistence are implemented, deploy the API to a .NET-compatible
host such as Azure App Service and use managed PostgreSQL. Choose pricing at that time.
Configure an HTTPS API URL for the frontend, exact allowed origins, secrets on the host,
database migrations, and authenticated SignalR. Frontend environment variables are public.
Keep backend workers running reliably; sleeping hosts can delay scheduled escalation.

For Pages, configure Vite's base as `/signaldesk/`, build with GitHub Actions, and publish
the dist directory. Use hash routing if client-side routes are added. The current starter
intentionally points to localhost and is not a public demo. A Pages-only demo would need
an explicitly labeled browser-storage mode; it would not demonstrate shared coordination.

## Learning rhythm

For each milestone: understand the data flow, implement one small change, run it,
test its failure case, commit it, and explain the result in your own words.
Document real measurements only. Do not claim concurrency or efficiency numbers until tested.

Official references:
- https://dotnet.microsoft.com/download/dotnet/10.0
- https://vite.dev/guide/
- https://docs.github.com/en/pages/getting-started-with-github-pages/what-is-github-pages
- https://learn.microsoft.com/aspnet/core/tutorials/min-web-api
- https://learn.microsoft.com/ef/core/get-started/overview/first-app
- https://learn.microsoft.com/aspnet/core/signalr/introduction
