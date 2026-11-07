# Run from PowerShell with .NET 10 and Docker available. Owns only the container it creates.
$ErrorActionPreference = 'Stop'
$testId = [Guid]::NewGuid().ToString('N')
$testDatabase = "prv_test_$testId"
$testPassword = [Guid]::NewGuid().ToString('N')
$containerId = $null
$previousConnection = $env:PRV_TEST_POSTGRES
$previousConsent = $env:PRV_TEST_POSTGRES_DISPOSABLE
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet restore ProductionReleaseValidation.sln
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet build ProductionReleaseValidation.sln --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    $dockerOutput = @(docker run --detach --rm --name "prv-tests-$testId" --label prv.disposable-test=true `
        --env POSTGRES_USER=prv_test_runner --env "POSTGRES_PASSWORD=$testPassword" `
        --env "POSTGRES_DB=$testDatabase" --publish 127.0.0.1::5432 `
        --tmpfs /var/lib/postgresql/data postgres:17)
    if ($LASTEXITCODE -ne 0) { throw 'Could not create disposable PostgreSQL container.' }
    $containerId = $dockerOutput[-1].Trim()
    if ($containerId -notmatch '\A[0-9a-f]{64}\z') {
        $containerId = $null
        throw 'Docker did not return a valid container ID.'
    }

    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        docker exec $containerId pg_isready --host=127.0.0.1 -U prv_test_runner -d $testDatabase *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw 'Disposable PostgreSQL did not become ready.' }
    $binding = docker port $containerId 5432/tcp
    if ($LASTEXITCODE -ne 0 -or $binding -notmatch '\A127\.0\.0\.1:(\d+)\z') {
        throw 'Unexpected disposable PostgreSQL port binding.'
    }
    $testPort = $Matches[1]
    $env:PRV_TEST_POSTGRES = "Host=127.0.0.1;Port=$testPort;Database=$testDatabase;Username=prv_test_runner;Password=$testPassword"
    $env:PRV_TEST_POSTGRES_DISPOSABLE = 'true'
    dotnet test ProductionReleaseValidation.sln --configuration Release --no-build --no-restore --logger trx
    if ($LASTEXITCODE -ne 0) { throw 'V1 tests failed.' }
}
finally {
    $env:PRV_TEST_POSTGRES = $previousConnection
    $env:PRV_TEST_POSTGRES_DISPOSABLE = $previousConsent
    if ($containerId) {
        # --rm removes only this newly created container and its ephemeral storage on stop.
        docker stop $containerId
        if ($LASTEXITCODE -ne 0) { Write-Warning "Could not stop disposable test container $containerId" }
    }
    Pop-Location
}
