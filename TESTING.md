# V1 automated tests

Requires .NET SDK 10, PowerShell on Windows, and Docker running Linux containers.
The complete `scripts/Test-V1.ps1` runner has been independently verified using
Windows PowerShell 5.1; PowerShell 7 is not required for that verified workflow.
Compatibility with other shells and platforms has not been verified. Run from
the repository root. No application database configuration, user secrets, hosted
database credentials, or existing PostgreSQL container is needed.

These tests verify this repository's application. They do not run checks for
releases recorded in the API: validation evidence in V1 is manually submitted.

## Complete local run (PowerShell)

```powershell
./scripts/Test-V1.ps1
```

This restores and builds the solution in Release, starts a new `postgres:17`
container with a random name, database, password and loopback port, then runs every
test. PostgreSQL data lives in temporary memory. The script stops only the container
ID returned by its own `docker run`, even if tests fail. Environment variables are
restored afterward. Repeating the command creates a fresh target each time.

Never use existing development, shared, client, production, or remote databases
for automated tests. Use only a fresh disposable local target. The script does
not use the application's `ConnectionStrings:Postgres` configuration.

## Existing tests without PostgreSQL

```powershell
dotnet test tests/ProductionReleaseValidation.UnitTests --configuration Release
dotnet test tests/ProductionReleaseValidation.IntegrationTests --configuration Release
```

The integration project uses an in-memory SQLite database. Release builds also avoid a
file-lock conflict when a manually started Debug API is still running.

## Supplying your own disposable target

The PostgreSQL project deliberately **fails**, rather than skips, if its target is
missing or unsafe. Set both `PRV_TEST_POSTGRES` (Npgsql connection string) and
`PRV_TEST_POSTGRES_DISPOSABLE=true`. The connection must specify:

- Host exactly `127.0.0.1`, `localhost` or `::1`; remote/multiple hosts are rejected.
- Username exactly `prv_test_runner`.
- Database `prv_test_` followed by exactly 32 lowercase hexadecimal characters.
- No `Options`, `Search Path` or multiplexing.

Use a freshly created, empty database for **each test invocation**. Before any
migrations, the fixture rejects existing user tables, sequences, views, materialized
views and foreign tables, including migration history. It never drops, resets,
truncates or deletes a database. The caller is responsible for disposing its target.
The local script and CI service supply this lifecycle automatically. The guard's
unit tests can run without a database:

```powershell
dotnet test tests/ProductionReleaseValidation.PostgresTests --configuration Release --filter FullyQualifiedName~PostgresTargetTests
```

## Coverage and CI

The PostgreSQL tests use the production Npgsql registration and `MigrateAsync`,
not `EnsureCreated`. They verify the committed InitialPostgres migration and
reapplication; all four API operations; metadata, evidence and calculated status
after complete application-host recreation; duplicate and conflicting submissions;
all three unique indexes and both foreign keys using PostgreSQL error codes and
constraint names; missing-resource responses; and coordinated concurrent validation
inserts that force the existing unique-violation recovery path.

A fixed seven-digit timestamp demonstrates that PostgreSQL/Npgsql retains six
fractional digits while the tracked .NET instance retains seven. Regression checks
require API-created timestamps and original/duplicate/reloaded evidence to agree
exactly. The API truncates its three generated UTC timestamps to microseconds;
the domain retains its supplied timestamps, while API timestamp generation
accounts for the database's precision.

`.github/workflows/v1-tests.yml` runs on pushes, pull requests and manual dispatch.
It uses a healthy temporary PostgreSQL 17 service with a dynamically allocated
port, installs .NET 10, restores, builds Release, and runs the entire solution
without filters or skips. TRX results are written under each project's TestResults
directory. GitHub removes the service after the job. The workflow has read-only
repository permissions and performs no publishing or deployment.

Precision reference: [Npgsql date/time handling](https://www.npgsql.org/doc/types/datetime).
