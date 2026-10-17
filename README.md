# SignalDesk

Turn scattered IT reports into a coordinated incident response.

SignalDesk is a campus IT reporting and incident-management application built
with React, TypeScript, ASP.NET Core 10, and PostgreSQL.

Reporters submit issues and follow related incident progress. Technicians
group reports into incidents, take ownership, update statuses, and review
activity history.

## Project status

SignalDesk currently runs locally. GitHub hosts the source code, and
GitHub Actions runs the API integration tests and frontend build on
pushes and pull requests.

There is no publicly hosted application yet.

## Features

### Reporter workflow

- Sign in with an existing account.
- Submit reports for Wi-Fi, Email, Printing, or Other services.
- View only reports belonging to the signed-in account.
- Search reports by title or location.
- Filter reports by service and status.
- View the title and status of a linked incident.

### Technician workflow

- View reports across accounts.
- Change report statuses: New, Investigating, and Resolved.
- Create incidents and link matching-service reports.
- Preserve each report's original contents when linking.
- Assign an incident to the signed-in technician.
- See technician usernames and “Assigned to you” labels.
- Filter incidents by title, service, status, and assignment.
- Use the My incidents filter.
- Release an assignment only when signed in as its owner.
- Update incident statuses independently of report statuses.
- Review timestamped activity history.

Admin-role accounts can also access technician endpoints. Assignment release
remains restricted to the assigned owner.

### Grouping suggestions

Technicians can request related-report suggestions and approve individual links.

The current rules:

- Service must match the incident.
- Reports must be unlinked and not Resolved.
- Submission time must be within two hours of a linked report.
- If the incident has no linked reports, its creation time is used instead.
- Matching a nearby linked report's location produces a Strong match.
- Matching service and time without a location match produces a Possible match.
- Location matching ignores capitalization and repeated whitespace.
- Resolved incidents return no suggestions.

The endpoint scores up to the 200 most recent candidate reports within the
matching time range and returns up to 10 suggestions.

Suggestions explain their reasons. They never link reports automatically.
Approved links use the existing version checks and activity history.

## Technology

| Layer | Technology |
| --- | --- |
| Frontend | React, TypeScript, Vite, CSS |
| API | ASP.NET Core 10 Minimal APIs |
| Authentication | ASP.NET Core Identity bearer tokens |
| Authorization | Reporter ownership filters and Technician/Admin roles |
| Database | PostgreSQL |
| Data access | Entity Framework Core and Npgsql |
| Concurrency | Version tokens and HTTP 409 responses |
| Testing | xUnit, WebApplicationFactory, Testcontainers |
| Automation | GitHub Actions |

## Requirements

For local development on Windows:

- Git
- VS Code
- Node.js 22.12+ in the 22.x line, or Node.js 24
- .NET 10 SDK
- Docker Desktop with Linux containers
- EF Core CLI for database migrations

The GitHub Actions frontend job uses Node.js 22.

Verify your installation in PowerShell:

```powershell
git --version
node --version
npm.cmd --version
dotnet --version
docker --version
```

If you do not already have the EF Core CLI:

```powershell
dotnet tool install --global dotnet-ef --version "10.*"
```

Verify it:

```powershell
dotnet ef --version
```

## Local setup

Run commands from the repository root unless stated otherwise.

### 1. Start PostgreSQL

Open Docker Desktop and wait for its engine to start.

```powershell
docker compose up -d --wait
```

Use the database name, username, password, and published port configured
in your local Compose setup.

### 2. Configure the database connection

Store the API connection string in .NET user secrets:

```powershell
dotnet user-secrets set "ConnectionStrings:SignalDesk" "Host=localhost;Port=<database-port>;Database=<database-name>;Username=<database-user>;Password=<database-password>" --project backend/SignalDesk.Api
```

Replace every placeholder with your local database settings.

Do not commit real connection strings, passwords, access tokens, or
authentication keys.

### 3. Apply database migrations

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet ef database update --project backend/SignalDesk.Api
```

Migrations create and update the application's database schema.
The API does not currently apply them automatically at startup.

### 4. Install frontend dependencies

```powershell
npm.cmd --prefix frontend ci
```

Commit `frontend/package-lock.json` so clean installations use the
recorded dependency versions.

## Run locally

Use two terminals and leave both running.

### Terminal 1: API

From the repository root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project backend/SignalDesk.Api --urls http://localhost:5080
```

### Terminal 2: frontend

```powershell
cd frontend
npm.cmd run dev -- --port 5173 --strictPort
```

Open:

- Frontend: http://localhost:5173
- API health: http://localhost:5080/health

The health endpoint checks that the API is responding. It does not
verify database connectivity.

## Create a local account

The current frontend provides sign-in. Account registration is available
through the Identity API.

With the API running, open a separate PowerShell terminal:

```powershell
$email = Read-Host "Account email"
$securePassword = Read-Host "Password: 12+ characters, uppercase, lowercase, number, symbol" -AsSecureString
$password = [System.Net.NetworkCredential]::new("", $securePassword).Password
$body = @{ email = $email; password = $password } | ConvertTo-Json

try {
    Invoke-RestMethod `
        -Uri "http://localhost:5080/auth/register" `
        -Method Post `
        -ContentType "application/json" `
        -Body $body
}
finally {
    Remove-Variable password, securePassword, body -ErrorAction SilentlyContinue
}
```

A successful registration normally returns no response body.
Then sign in through the frontend.

New accounts do not automatically receive Technician or Admin access.

## Give a local account Technician access

This bootstrap is available only in Development.

1. Register the account first.
2. Stop the API with Ctrl+C.
3. Configure the registered account's email:

```powershell
dotnet user-secrets set "Development:TechnicianEmail" "your-registered-email@example.com" --project backend/SignalDesk.Api
```

4. Restart the API:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project backend/SignalDesk.Api --urls http://localhost:5080
```

5. Sign out and sign back in to obtain a token with the assigned role.
6. Confirm that incident controls are available.
7. Remove the temporary bootstrap setting:

```powershell
dotnet user-secrets remove "Development:TechnicianEmail" --project backend/SignalDesk.Api
```

The role remains stored in PostgreSQL.

Restart the API after removing the setting so the next startup no longer
uses the bootstrap configuration.

## Demo workflow

Use fictional reports and separate Reporter and Technician accounts.

1. As Reporter, submit two Wi-Fi reports at `STEM 212`.
2. Confirm the account cannot see another reporter's reports.
3. Sign in as Technician.
4. Create a Wi-Fi incident.
5. Manually link the first report.
6. Open Suggested reports.
7. Review the second report's match reasons and approve its link.
8. Assign the incident to yourself.
9. Change the incident to Investigating.
10. Review the activity history.
11. Change the incident to Resolved.
12. Sign in as Reporter and refresh the reports.
13. Confirm the linked incident summary shows Resolved.

Report status and incident status are separate. Resolving an incident
does not automatically resolve its linked reports.

## Build and test

Stop the running API before building or testing on Windows.
Otherwise, its executable may be locked.

Keep Docker Desktop running.

From the repository root:

```powershell
dotnet build backend/SignalDesk.Api
npm.cmd --prefix frontend run build
dotnet test backend/SignalDesk.Api.Tests/SignalDesk.Api.Tests.csproj --logger "console;verbosity=normal"
```

### Integration-test coverage

The current suite contains 13 tests covering:

- Authentication requirements.
- Reporter ownership isolation.
- Technician access to reports.
- Rejection of unauthorized management requests.
- Owner-only assignment release.
- Stale-version conflicts.
- Concurrent updates with one successful writer.
- Activity history for successful changes.
- Public incident summaries on reporter-owned reports.
- Strong and Possible suggestion matches.
- Exclusion of wrong-service, resolved, and linked reports.
- Exclusion of reports outside the matching time window.
- Suggestion endpoint authorization.
- Approved links disappearing from suggestions.
- Empty suggestions for resolved incidents.

The tests create temporary PostgreSQL containers and use real
registration and login.

They do not require a manually running API or frontend and do not use
the normal development database.

The fixture creates its schema from the EF Core model. These tests do
not validate the migration history or browser interface.

Container shutdown and deletion messages at the end are normal cleanup.

## GitHub Actions

The workflow is in `.github/workflows/ci.yml`.

It runs on:

- Pushes.
- Pull requests.
- Manual workflow dispatch.

Two jobs run independently:

1. Backend restore, Release build, and integration tests.
2. Frontend dependency installation, TypeScript checks, and production build.

The tests create their own PostgreSQL containers. Database credentials
from local user secrets are not needed in GitHub Actions.

A green workflow confirms the configured build and test checks passed.
It does not deploy the application.

## Configuration

### Frontend

`frontend/src/AuthGate.tsx` reads `VITE_API_URL`.

- Development defaults to `http://localhost:5080`.
- Production builds default to same-origin API requests.
- A separately hosted API requires its public HTTPS URL at build time.

Example development setting:

```dotenv
VITE_API_URL=http://localhost:5080
```

Restart Vite after changing environment files.

Frontend `VITE_` variables are public build configuration. Never put
database passwords or private keys in them.

### Backend

| Configuration key | Environment-variable form | Purpose |
| --- | --- | --- |
| `ConnectionStrings:SignalDesk` | `ConnectionStrings__SignalDesk` | PostgreSQL connection |
| `Cors:AllowedOrigins:0` | `Cors__AllowedOrigins__0` | Allowed frontend origin |
| `DataProtection:KeysPath` | `DataProtection__KeysPath` | Persistent authentication-key directory |
| `Development:TechnicianEmail` | `Development__TechnicianEmail` | Development-only role bootstrap |

Development CORS defaults allow:

- `http://localhost:5173`
- `http://localhost:5174`

Outside Development, cross-origin frontend access must be configured
explicitly with HTTPS origins.

Listening addresses come from `--urls` or hosting configuration.

Outside Development, the API requires a Data Protection key directory.
For deployment, that directory must be private and persistent.
File persistence alone does not encrypt the key files.

Changing the Data Protection application name or losing its keys can
invalidate existing tokens.

## Current limitations

- No public deployment.
- No organization or tenant isolation; technician access is application-wide.
- No real-time updates; use Refresh to fetch changes.
- Refreshing the browser requires signing in again.
- No frontend registration or password-reset interface.
- No automatic synchronization of report and incident statuses.
- No saved suggestion dismissal or rejection workflow.
- No report unlinking or incident deletion interface.
- No pagination; current list filters operate on loaded records.
- No email notifications, background jobs, or transactional outbox.
- No browser end-to-end tests or automated migration tests.

Grouping suggestions are deterministic rules, not an AI model.
They identify possible relationships and require technician approval.

## Troubleshooting

### API executable is locked during a build

Stop the API terminal with Ctrl+C.

If the terminal cannot be found:

```powershell
Get-Process -Name SignalDesk.Api -ErrorAction SilentlyContinue |
    Stop-Process
```

Then rebuild or rerun the tests.

### Frontend port 5173 is already in use

Check whether SignalDesk is already running at
http://localhost:5173.

To restart, stop the existing Vite terminal with Ctrl+C, then run
the frontend command again.

### Sign-in cannot reach the API

- Confirm http://localhost:5080/health responds.
- Check the browser Network tab.
- Confirm requests target port 5080.
- Check any `VITE_API_URL` override.
- Restart Vite after changing environment files.

### A request returns 403

The account is authenticated but lacks permission for that action.

Use a Technician account for incident management. After assigning a role,
sign out and sign back in.

Only the assigned owner can release an incident assignment.

### A request returns 409

The record changed after the frontend loaded it.

Refresh and retry using the latest version. Do not bypass version checks.

### No suggestions appear

Check that:

- The incident is not Resolved.
- Candidate reports are unlinked and not Resolved.
- Services match.
- Submission times fall within the two-hour window.

Create fresh reports when demonstrating the feature.

## Main source files

| File | Responsibility |
| --- | --- |
| `backend/SignalDesk.Api/Program.cs` | Configuration, authentication, report endpoints |
| `backend/SignalDesk.Api/SignalDeskDbContext.cs` | Database mappings and concurrency tokens |
| `backend/SignalDesk.Api/IncidentEndpoints.cs` | Incident management and activity history |
| `backend/SignalDesk.Api/IncidentSuggestionEndpoints.cs` | Suggestion selection and ranking |
| `frontend/src/AuthGate.tsx` | Sign-in, in-memory token, authenticated requests |
| `frontend/src/main.tsx` | Report UI, permissions, and navigation |
| `frontend/src/IncidentPanel.tsx` | Incident UI, filters, and ownership |
| `frontend/src/IncidentTimeline.tsx` | Activity timeline |
| `frontend/src/IncidentSuggestions.tsx` | Suggestion review and approval |
| `backend/SignalDesk.Api.Tests/` | API integration tests |
| `.github/workflows/ci.yml` | Automated builds and tests |

## Next milestones

- Validate migrations against a fresh PostgreSQL database.
- Add browser end-to-end tests.
- Improve token-expiry handling.
- Add pagination and server-side filtering.
- Add organization boundaries if multi-organization use is required.
- Add authenticated real-time updates.
- Add reliable notifications and background processing.
- Prepare production account provisioning, HTTPS, key protection, and backups.
- Deploy a demo when a hosting provider is selected.

GitHub Pages could host the static frontend, but the .NET API and
PostgreSQL would still require separate hosting.

## Development approach

Implement one workflow at a time, test its failure cases, and commit a
working milestone.

Document measured results only. Passing tests demonstrate the covered
scenarios; they are not performance benchmarks.