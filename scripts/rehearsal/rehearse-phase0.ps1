<#
.SYNOPSIS
    Rehearses the Phase 0 schema change end to end on a DISPOSABLE database (Phase 0 step 0.10,
    design section 11): baseline schema + synthetic data, inspect, back up, apply the bundle,
    verify, smoke-test the app, roll back (R2), re-apply, restore the backup (R3). Times every step
    and checks that the approved app's data is identical throughout.

.PARAMETER Server
    SQL Server instance. Default: LocalDB.
.PARAMETER Database
    Name of the disposable database. Must contain "Rehearsal"; it is DROPPED and recreated.
.PARAMETER AppDll
    Published or built InpremClockingApp.dll for the smoke test (skipped when not given).
.PARAMETER Keep
    Keep the database afterwards (it is dropped by default).
.PARAMETER Existing
    Rehearse on an existing database instead of a synthetic one: a RESTORED COPY of the real
    database (design 11.2-11.3). Skips creating and seeding; everything else is the same. Its name
    must still contain "Rehearsal".

.EXAMPLE
    pwsh scripts/rehearsal/rehearse-phase0.ps1 -AppDll InpremClockingApp/bin/Release/net8.0/InpremClockingApp.dll
#>
param(
    [string]$Server = '(localdb)\MSSQLLocalDB',
    [string]$Database = 'InpremPhase0Rehearsal',
    [string]$EfTool = (Join-Path $env:USERPROFILE '.dotnet\ef8\dotnet-ef.exe'),
    [string]$AppDll,
    [switch]$Keep,
    [switch]$Existing
)
$ErrorActionPreference = 'Stop'
if ($Database -notmatch 'Rehearsal') { throw "Refusing: the database name must contain 'Rehearsal' (it is dropped)." }

$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$migrations = Join-Path $root 'scripts/migrations'
$baseline = '20261003091001_AddVolunteerContactPersonDetails'
$connection = "Server=$Server;Database=$Database;Integrated Security=true;TrustServerCertificate=true"
$results = [System.Collections.Generic.List[object]]::new()

function Sql([string]$file, [string]$query, [string]$db = $Database) {
    # ConfirmLocalCopy: the inspection scripts' guard; this database is disposable by construction.
    $args = @('-S', $Server, '-d', $db, '-E', '-I', '-b', '-h', '-1', '-W', '-s', '|', '-v', 'ConfirmLocalCopy=YES')
    $args += if ($file) { @('-i', $file) } else { @('-Q', "SET NOCOUNT ON; $query") }
    $out = & sqlcmd @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed ($file$query):`n$($out -join "`n")" }
    return ($out | Where-Object { $_ -and $_ -notmatch '^\s*$' }) -join "`n"
}

function Step([string]$name, [scriptblock]$action) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $detail = & $action
    $watch.Stop()
    $results.Add([pscustomobject]@{ Step = $name; Seconds = [math]::Round($watch.Elapsed.TotalSeconds, 1); Result = ($detail -join ' ') })
    Write-Host ("{0,-48} {1,7:N1}s  {2}" -f $name, $watch.Elapsed.TotalSeconds, ($detail -join ' '))
}

function Fingerprint { Sql (Join-Path $root 'scripts/rehearsal/fingerprint.sql') $null }
function Same([string]$when) {
    $now = Fingerprint
    if ($now -ne $script:before) {
        $diff = Compare-Object ($script:before -split "`n") ($now -split "`n") | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }
        throw "Fingerprint changed $when`n$($diff -join "`n")"
    }
    'identical'
}
function Verify {
    @('platform-M1a', 'platform-M2', 'app-M3', 'app-M4', 'platform-M1b', 'hangfire') | ForEach-Object {
        $lines = (Sql (Join-Path $migrations "$_-verify.sql") $null) -split "`n"
        "$_=$($lines[0])"
    }
}
function AppHistory { Sql $null "SELECT TOP 1 MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC" }

$backup = Join-Path ([IO.Path]::GetTempPath()) "$Database.bak"

if ($Existing) {
    Step 'Use existing restored copy' { "last app migration: $(AppHistory)" }
} else {
Step 'Create disposable database (baseline schema)' {
    Sql $null "IF DB_ID(N'$Database') IS NOT NULL BEGIN ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database]; END; CREATE DATABASE [$Database];" 'master' | Out-Null
    Push-Location (Join-Path $root 'InpremClockingApp')
    try { & $EfTool database update $baseline --context ApplicationDbContext --connection $connection | Out-Null; if ($LASTEXITCODE) { throw 'ef database update failed' } }
    finally { Pop-Location }
    "last app migration: $(AppHistory)"
}

Step 'Seed synthetic data' {
    Sql (Join-Path $root 'scripts/rehearsal/seed-rehearsal-data.sql') $null | Out-Null
    $c = Sql $null "SELECT CONCAT((SELECT COUNT(*) FROM Tenants), ' orgs, ', (SELECT COUNT(*) FROM AspNetUsers), ' admins, ', (SELECT COUNT(*) FROM Staffs) + (SELECT COUNT(*) FROM Volunteers), ' people, ', (SELECT COUNT(*) FROM ClockingsStaff) + (SELECT COUNT(*) FROM Clockings), ' sessions')"
    $c
}
}

$before = $null
Step '1 Inspect (data quality, fingerprint)' {
    $quality = Sql (Join-Path $root 'scripts/inspection/04-data-quality.sql') $null
    $script:before = Fingerprint
    "data-quality output lines: $(($quality -split "`n").Count); fingerprint rows: $(($script:before -split "`n").Count)"
}

Step '2 Back up (copy-only, checksum) + SHA-256' {
    Sql $null "BACKUP DATABASE [$Database] TO DISK = N'$backup' WITH COPY_ONLY, CHECKSUM, INIT, FORMAT;" 'master' | Out-Null
    $file = Get-Item $backup
    "{0:N1} MB, sha256 {1}" -f ($file.Length / 1MB), (Get-FileHash $backup -Algorithm SHA256).Hash.Substring(0, 16)
}

Step '3 Apply phase0-up.sql' { (Sql (Join-Path $migrations 'phase0-up.sql') $null) -split "`n" | Select-Object -Last 1 }
Step '  verify scripts' { Verify }
Step '  app data unchanged (fingerprint)' { Same 'after up' }
Step '  apply again (idempotent)' { (Sql (Join-Path $migrations 'phase0-up.sql') $null) -split "`n" | Select-Object -Last 1 }
Step '  app data unchanged after re-run' { Same 'after second up' }

if ($AppDll) {
    Step '  smoke test: app on the migrated database' {
        $port = 5199
        $env:ConnectionStrings__DefaultConnection = $connection
        $env:Site__SiteMode = 'All'
        $env:Jobs__RunServer = 'true'
        $env:ASPNETCORE_URLS = "http://127.0.0.1:$port"
        $env:ASPNETCORE_ENVIRONMENT = 'Rehearsal'
        $app = Start-Process dotnet -ArgumentList "`"$AppDll`"" -WorkingDirectory (Split-Path $AppDll) -PassThru -WindowStyle Hidden
        try {
            $handler = [System.Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false
            $http = [System.Net.Http.HttpClient]::new($handler); $http.BaseAddress = "http://127.0.0.1:$port"
            function Get([string]$path) { $http.GetAsync($path).GetAwaiter().GetResult() }
            $deadline = (Get-Date).AddSeconds(60); $up = $false
            while (-not $up -and (Get-Date) -lt $deadline) {
                try { $null = Get '/Identity/Account/Login'; $up = $true } catch { Start-Sleep -Milliseconds 500 }
            }
            if (-not $up) { throw 'The app did not start.' }
            $login = [int](Get '/Identity/Account/Login').StatusCode
            $setup = [int](Get '/Provider/setup').StatusCode
            $health = (Get '/health').Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $back = [int](Get '/BackOffice').StatusCode
            Start-Sleep -Seconds 3
            $servers = ((Sql (Join-Path $migrations 'hangfire-verify.sql') $null) -split '\|')[2]
            if ($login -ne 200 -or $setup -ne 404 -or $back -ne 302 -or $health -ne 'Degraded' -or [int]$servers -lt 1) {
                throw "Smoke failed: login $login, setup $setup, backoffice $back, health $health, job servers $servers"
            }
            "login 200, /Provider/setup 404, /BackOffice 302 (sign-in), /health $health, job servers $servers"
        }
        finally {
            Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
            'ConnectionStrings__DefaultConnection', 'Site__SiteMode', 'Jobs__RunServer', 'ASPNETCORE_URLS', 'ASPNETCORE_ENVIRONMENT' | ForEach-Object { Remove-Item "Env:$_" -ErrorAction SilentlyContinue }
        }
    }
}

Step '4 Rollback R2: phase0-down.sql' { (Sql (Join-Path $migrations 'phase0-down.sql') $null) -split "`n" | Select-Object -Last 1 }
Step '  verify scripts' { Verify }
Step '  app data unchanged, history at baseline' {
    Same 'after down' | Out-Null
    if ((AppHistory) -ne $baseline) { throw "App history at $(AppHistory)." }
    "identical; last app migration $(AppHistory)"
}
Step '  rollback again (idempotent)' { (Sql (Join-Path $migrations 'phase0-down.sql') $null) -split "`n" | Select-Object -Last 1 }

Step '5 Re-apply phase0-up.sql' { (Sql (Join-Path $migrations 'phase0-up.sql') $null) -split "`n" | Select-Object -Last 1 }
Step '  app data unchanged' { Same 'after re-apply' }

Step '6 Rollback R3: restore the backup' {
    Sql $null "ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [$Database] FROM DISK = N'$backup' WITH REPLACE, CHECKSUM; ALTER DATABASE [$Database] SET MULTI_USER;" 'master' | Out-Null
    Same 'after restore' | Out-Null
    "identical; last app migration $(AppHistory); platform schema $(Sql $null "SELECT CASE WHEN SCHEMA_ID(N'platform') IS NULL THEN 'absent' ELSE 'present' END")"
}

Step 'Guard: refuses a database that is not the app' {
    $other = "${Database}_Wrong"
    Sql $null "IF DB_ID(N'$other') IS NULL CREATE DATABASE [$other];" 'master' | Out-Null
    $out = & sqlcmd -S $Server -d $other -E -I -b -i (Join-Path $migrations 'phase0-up.sql') 2>&1
    $code = $LASTEXITCODE
    Sql $null "ALTER DATABASE [$other] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$other];" 'master' | Out-Null
    if ($code -eq 0 -or ($out -join ' ') -notmatch 'wrong database') { throw 'The guard did not stop the bundle on a wrong database.' }
    "stopped (exit $code): wrong database"
}

if (-not $Keep) {
    Sql $null "ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database];" 'master' | Out-Null
    Remove-Item $backup -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host '| Step | Seconds | Result |'
Write-Host '|---|---:|---|'
$results | ForEach-Object { Write-Host "| $($_.Step.Trim()) | $($_.Seconds) | $($_.Result -replace '\|', '/') |" }
