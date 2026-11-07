# Production Release Validation

A small HTTP API that records validation evidence against a software release and
calculates its release status. It gives a service's version, commit reference,
and reported validation outcomes one consistent place to live.

V1 is a portfolio project demonstrating domain modeling, explicit API contracts,
relational persistence, conflict handling, and tests against real PostgreSQL.
It is intended for engineers and release coordinators exploring how to record
release evidence, and for reviewers evaluating the implementation. Example uses
include recording the reported test and performance results for a release
candidate, then recording a deployment health result after a separate deployment.

**Validation evidence is manually submitted.** The application does not independently
run automated tests, deploy software, monitor deployment health, or verify submitted
claims. `AutomatedTests`, `Performance`, and `DeploymentHealth` are evidence categories.
`Verified` describes the stored outcomes, not an independently certified release.

**V1 has no application authentication or authorization.** Use it locally with
synthetic data and trusted users. It is not production-ready for untrusted public
exposure. Read [security limitations](SECURITY.md) before running it.

## Architecture and stack

The request path is ASP.NET Core Minimal API → domain objects → EF Core → PostgreSQL.
The domain calculates status from the evidence; status is not a separately stored
field or a caller-controlled value.

| Component | Implementation |
| --- | --- |
| Runtime and API | C#, .NET 10, ASP.NET Core Minimal APIs |
| Persistence | EF Core 10.0.12; Npgsql EF Core provider 10.0.3 |
| Database | PostgreSQL; version 17 is used by the local test runner and CI |
| Tests | xUnit; in-process HTTP tests using SQLite; real PostgreSQL integration tests |
| CI | GitHub Actions restores, builds, and tests this repository; no deployment |

```text
src/ProductionReleaseValidation.Api/
  Domain/                         Service, Release, ValidationResult and enums
  Endpoints/                      Four routes and request/response contracts
  Infrastructure/Persistence/     DbContext, mappings and committed migrations
tests/
  ProductionReleaseValidation.UnitTests/
  ProductionReleaseValidation.IntegrationTests/   SQLite-backed API tests
  ProductionReleaseValidation.PostgresTests/      Production-provider tests
scripts/Test-V1.ps1                Complete disposable-database test run
.github/workflows/v1-tests.yml    CI test workflow
```

PostgreSQL enforces unique service names, unique versions per service, and one
validation result per release/type, plus service/release foreign keys. API-generated
IDs are UUIDv7; timestamps are server UTC, truncated to PostgreSQL microsecond
precision so initial and reloaded responses agree.

## Local prerequisites and setup

Use .NET SDK 10, PowerShell on Windows, Docker running Linux containers, and access
to NuGet and the `postgres:17` image. Run commands from the repository root.
The complete `scripts/Test-V1.ps1` runner has been independently verified using
Windows PowerShell 5.1; PowerShell 7 is not required for that verified workflow.
Compatibility with other shells and platforms has not been verified.
The instructions below use PowerShell on Windows; ports 5243 and the dynamically
allocated database port must be available on loopback.

```powershell
dotnet --version
docker version
dotnet restore ProductionReleaseValidation.sln
dotnet build ProductionReleaseValidation.sln --configuration Release --no-restore
```

### Start a disposable local PostgreSQL database (terminal A)

This example creates a new database with a random local-only password and a
random loopback port. Its data is temporary and disappears when the container
stops. Keep terminal A open for the migration and application commands below.
Do not substitute a development, shared, client, or production database.

```powershell
$ErrorActionPreference = 'Stop'
$demoId = [Guid]::NewGuid().ToString('N')
$demoPassword = [Guid]::NewGuid().ToString('N')
$demoContainer = docker run --detach --rm --name "prv-demo-$demoId" `
    --env POSTGRES_USER=prv_demo --env "POSTGRES_PASSWORD=$demoPassword" `
    --env POSTGRES_DB=prv_demo --publish 127.0.0.1::5432 `
    --tmpfs /var/lib/postgresql/data postgres:17
if ($LASTEXITCODE -ne 0) { throw 'Could not start the demo database.' }
$demoContainer = $demoContainer.Trim()

$demoReady = $false
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    docker exec $demoContainer pg_isready --host=127.0.0.1 -U prv_demo -d prv_demo *> $null
    if ($LASTEXITCODE -eq 0) { $demoReady = $true; break }
    Start-Sleep -Seconds 1
}
if (-not $demoReady) { throw 'Demo database is not ready; see cleanup below.' }
$demoBinding = docker port $demoContainer 5432/tcp
if ($LASTEXITCODE -ne 0 -or $demoBinding -notmatch '^127\.0\.0\.1:(\d+)$') {
    throw 'Unexpected demo database port binding; see cleanup below.'
}
$demoPort = $Matches[1]
$env:ConnectionStrings__Postgres = "Host=127.0.0.1;Port=$demoPort;Database=prv_demo;Username=prv_demo;Password=$demoPassword"
```

The API reads `ConnectionStrings:Postgres` through standard .NET configuration;
`ConnectionStrings__Postgres` is the environment variable form. No connection
string is committed. The application fails at startup if it is missing.
For a separate persistent local setup, .NET user secrets are also supported when
running in `Development` (set `$env:ASPNETCORE_ENVIRONMENT = 'Development'`):
the key is `ConnectionStrings:Postgres`, and the project is
`src/ProductionReleaseValidation.Api`. Keep credentials out of tracked files.
The application does not load `.env` files automatically.

### Apply PostgreSQL migrations (terminal A)

Install a repository-local EF CLI matching the EF Core design package. Its output
is under the already ignored `artifacts/` directory; no global tool change is needed.
Run the install once; reuse the tool on later runs.

```powershell
dotnet tool install dotnet-ef --version 10.0.12 --tool-path ./artifacts/tools
if ($LASTEXITCODE -ne 0) { throw 'EF tool installation failed.' }
./artifacts/tools/dotnet-ef database update --project src/ProductionReleaseValidation.Api --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Database migration failed.' }
```

The committed migration is `20261106182901_InitialPostgres`. It creates `Services`,
`Releases`, `ValidationResults`, their foreign keys, and three unique indexes.
`database update` uses EF migration history, so repeating it against the same
migrated database is safe. The API **does not apply migrations on startup**.
Use the committed migration rather than `EnsureCreated` for the application database.

### Run the application (terminal A)

```powershell
dotnet run --project src/ProductionReleaseValidation.Api --configuration Release --no-launch-profile --urls http://127.0.0.1:5243
```

Wait for the listening message, then use terminal B below. The explicit loopback
URL avoids launch-profile browser behavior and HTTPS certificate setup for this
local demo. There is no home page, Swagger UI, or health endpoint; `/` returns 404.
If port 5243 is already occupied, choose a free loopback port and change it in
both the run command and terminal B's `$base` value. Leave existing processes alone.

## Reproducible local demonstration (terminal B)

This uses synthetic evidence and a synthetic commit reference. It creates a unique
service name each run, records three manually asserted passes, and checks status
after every operation. It does not execute a test suite or perform a deployment.

```powershell
$ErrorActionPreference = 'Stop'
$base = 'http://127.0.0.1:5243'
function Post-DemoJson($path, $body) {
    Invoke-RestMethod -Method Post -Uri "$base$path" -ContentType 'application/json' `
        -Body ($body | ConvertTo-Json -Depth 5)
}
$service = Post-DemoJson '/services' @{ name = "orders-demo-$([Guid]::NewGuid().ToString('N'))" }
$release = Post-DemoJson '/releases' @{
    serviceId = $service.id
    version = '1.0.0'
    commitSha = '0123456789abcdef0123456789abcdef01234567'
}
if ($release.status -ne 'Pending') { throw 'Expected Pending.' }
"Created: $($release.status)"

$steps = @(
    @{ type = 'AutomatedTests'; expected = 'Pending' },
    @{ type = 'Performance'; expected = 'ReadyForDeployment' },
    @{ type = 'DeploymentHealth'; expected = 'Verified' }
)
foreach ($step in $steps) {
    $evidence = Post-DemoJson "/releases/$($release.id)/validations" @{
        type = $step.type
        outcome = 'Passed'
        summary = 'Synthetic manually submitted demo evidence; no checks executed.'
        evidenceUrl = 'https://example.com/synthetic-evidence'
    }
    $current = Invoke-RestMethod "$base/releases/$($release.id)"
    if ($current.status -ne $step.expected) { throw "Unexpected status: $($current.status)" }
    "$($step.type): $($current.status)"
}

# Same type and outcome returns the original evidence, ignoring new metadata.
$retry = Post-DemoJson "/releases/$($release.id)/validations" @{
    type = 'DeploymentHealth'; outcome = 'Passed'; summary = 'Ignored retry metadata'
}
if ($retry.id -ne $evidence.id -or $retry.summary -ne $evidence.summary) {
    throw 'Expected the original evidence on retry.'
}
$current | ConvertTo-Json -Depth 5

# A separate candidate demonstrates failure taking precedence immediately.
$failedRelease = Post-DemoJson '/releases' @{
    serviceId = $service.id; version = '1.0.1'
    commitSha = '0123456789abcdef0123456789abcdef01234567'
}
$null = Post-DemoJson "/releases/$($failedRelease.id)/validations" @{
    type = 'AutomatedTests'; outcome = 'Failed'; summary = 'Synthetic failure example'
}
$failed = Invoke-RestMethod "$base/releases/$($failedRelease.id)"
if ($failed.status -ne 'Failed') { throw 'Expected Failed.' }
"Separate candidate: $($failed.status)"
```

Expected progression: `Pending → Pending → ReadyForDeployment → Verified`.
The separate candidate becomes `Failed`. The evidence link is an illustrative
placeholder; the application never opens it.

When finished, press Ctrl+C in terminal A to stop the API, then run the following
in **terminal A**, including after a setup failure if a container was created:

```powershell
docker stop $demoContainer
Remove-Item Env:ConnectionStrings__Postgres -ErrorAction SilentlyContinue
$demoPassword = $null
```

Stop only the container ID captured by this demo. Restarting the demo database
requires applying migrations again because its storage is ephemeral.

## Run all tests

```powershell
./scripts/Test-V1.ps1
```

This restores and builds Release, creates its own isolated PostgreSQL 17 container,
runs the entire solution, and stops that container in `finally`. It does not use
the demo connection string or any existing database. Run it from another terminal
if the demo API is still open. See [TESTING.md](TESTING.md) for prerequisite details,
target safety guards, smaller test runs, and CI behavior.

The repository's automated tests test **this application**. They are separate from
the manually submitted `AutomatedTests` evidence recorded through the API.

## The four API operations

Requests and successful responses use JSON (`application/json`); send
`Content-Type: application/json` on POSTs. Property names in responses are camelCase,
and enum values are strings. The examples below show one release after its first
validation; generated IDs and times are illustrative. Use IDs returned by your own
requests, as the demo does.

| Operation | Success | Other relevant responses |
| --- | --- | --- |
| `POST /services` | 201, service | 400 invalid request; 409 duplicate name |
| `POST /releases` | 201, release | 400 invalid request; 404 missing service; 409 duplicate version for service |
| `POST /releases/{id}/validations` | 201 new evidence; 200 original evidence on same-outcome retry | 400 invalid request/ID; 404 missing release; 409 conflicting outcome |
| `GET /releases/{id}` | 200, release with evidence and calculated status | 400 malformed ID; 404 missing release |

### Register a service

`POST /services`

```json
{ "name": "orders-api" }
```

201 response (`Location: /services/019a0000-0000-7000-8000-000000000001`):

```json
{
  "id": "019a0000-0000-7000-8000-000000000001",
  "name": "orders-api",
  "createdAtUtc": "2026-11-06T20:00:00+00:00"
}
```

`name` is required, nonblank, and at most 100 characters. Uniqueness uses stored
text with the configured database's comparison rules; input is not trimmed or
normalized. The returned `Location` identifies the service, but V1 has **no
`GET /services/{id}` operation**. Retain the returned service ID to create releases.

### Create a release

`POST /releases`

```json
{
  "serviceId": "019a0000-0000-7000-8000-000000000001",
  "version": "1.0.0",
  "commitSha": "0123456789abcdef0123456789abcdef01234567"
}
```

201 response (`Location: /releases/019a0000-0000-7000-8000-000000000002`):

```json
{
  "id": "019a0000-0000-7000-8000-000000000002",
  "serviceId": "019a0000-0000-7000-8000-000000000001",
  "version": "1.0.0",
  "commitSha": "0123456789abcdef0123456789abcdef01234567",
  "createdAtUtc": "2026-11-06T20:01:00+00:00",
  "status": "Pending",
  "validationResults": []
}
```

`serviceId` must be a nonempty UUID for an existing service. `version` is required,
nonblank text of at most 50 characters; `commitSha` is required, nonblank text of
at most 64 characters. V1 does not enforce SemVer, hexadecimal SHA formatting,
or the existence of a Git commit. A version is unique within its service.

### Submit validation evidence

`POST /releases/019a0000-0000-7000-8000-000000000002/validations`

```json
{
  "type": "AutomatedTests",
  "outcome": "Passed",
  "summary": "Synthetic manually submitted demo evidence; no checks executed.",
  "evidenceUrl": "https://example.com/synthetic-evidence"
}
```

201 response (`Location: /releases/019a0000-0000-7000-8000-000000000002`):

```json
{
  "id": "019a0000-0000-7000-8000-000000000003",
  "releaseId": "019a0000-0000-7000-8000-000000000002",
  "type": "AutomatedTests",
  "outcome": "Passed",
  "recordedAtUtc": "2026-11-06T20:02:00+00:00",
  "summary": "Synthetic manually submitted demo evidence; no checks executed.",
  "evidenceUrl": "https://example.com/synthetic-evidence"
}
```

`type` and `outcome` are required. Types are `AutomatedTests`, `Performance`, and
`DeploymentHealth`; outcomes are `Passed` and `Failed`. Numeric enum values are
rejected. `summary` (at most 500 characters) and `evidenceUrl` (at most 2048) are
optional and may be null. The URL field is stored text: it is not URL-validated,
fetched, or checked for evidence authenticity.

There is at most one accepted result per release/type. A valid retry with the
same type and outcome returns **200 with the original result**, preserving its
ID, timestamp, summary, and URL even if new metadata differs. An opposing outcome
returns **409**. The first persisted evidence wins, including concurrent
submissions. This operation returns evidence only; GET the release to see status.

### Retrieve a release

`GET /releases/019a0000-0000-7000-8000-000000000002` has no request body.

200 response after the preceding submission:

```json
{
  "id": "019a0000-0000-7000-8000-000000000002",
  "serviceId": "019a0000-0000-7000-8000-000000000001",
  "version": "1.0.0",
  "commitSha": "0123456789abcdef0123456789abcdef01234567",
  "createdAtUtc": "2026-11-06T20:01:00+00:00",
  "status": "Pending",
  "validationResults": [
    {
      "id": "019a0000-0000-7000-8000-000000000003",
      "releaseId": "019a0000-0000-7000-8000-000000000002",
      "type": "AutomatedTests",
      "outcome": "Passed",
      "recordedAtUtc": "2026-11-06T20:02:00+00:00",
      "summary": "Synthetic manually submitted demo evidence; no checks executed.",
      "evidenceUrl": "https://example.com/synthetic-evidence"
    }
  ]
}
```

Evidence is ordered by `recordedAtUtc`, then ID. There are no list, update, or
delete operations in V1. Retain IDs from creation responses.

### Error responses

Errors use Problem Details JSON (`application/problem+json`). For example, a
different outcome for an already recorded type returns 409 with these fields:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Conflict",
  "status": 409,
  "detail": "A different outcome is already recorded for this validation type."
}
```

Validation errors return 400 with an `errors` object keyed by request property
names. Malformed JSON, UUIDs, or unsupported enums also return 400; do not depend
on an `errors` object for every kind of 400. Unexpected exceptions return a
generic 500 response. Framework-added diagnostic fields can vary.

## How release status is calculated

Rules are evaluated in this order, regardless of submission order:

| Condition in stored evidence | Status |
| --- | --- |
| Any result has outcome `Failed` | `Failed` |
| All three types have outcome `Passed` | `Verified` |
| `AutomatedTests` and `Performance` have outcome `Passed` | `ReadyForDeployment` |
| Anything else, including no results | `Pending` |

For example, a passed `DeploymentHealth` result alone leaves a release `Pending`.
A failure takes precedence even when other types pass. Status is recomputed from
persisted evidence after application restart; it is not a deployment command,
approval, live health measurement, or security certification.

## Tradeoffs and V1 limitations

- A single API project keeps the domain, HTTP boundary, and persistence mapping
  easy to inspect. It has no dashboard, background workers, or external integrations.
- First-result retention makes same-outcome retries idempotent and protects the
  original evidence metadata. V1 cannot amend, replace, or delete a result through
  the API. A failed type cannot later be changed to passed on that release; record
  a distinct release version for another candidate.
- Fixed validation categories and simple status rules make decisions reproducible.
  There are no configurable gates, scores, environment distinctions, or freshness
  rules; old evidence does not expire automatically.
- Required fields, length limits, relational constraints, and generic error
  responses provide bounded input contracts. They do not establish identity,
  evidence integrity, tenant isolation, or readiness for public operation.
- Fast SQLite HTTP tests complement real PostgreSQL tests; SQLite is not the
  application's production persistence provider. The full test runner exercises
  migrations, constraints, restart persistence, and concurrent insert handling.

## Licensing and public scope

This project is licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE).
Qualifying noncommercial uses are permitted under the license; uses outside its
permissions require separate permission. Dependency licenses remain separate.

This portfolio publishes application behavior, implementation choices, and
synthetic examples. It does not include client assessment materials or a consulting
delivery methodology.
