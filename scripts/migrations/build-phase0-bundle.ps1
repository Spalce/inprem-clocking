<#
.SYNOPSIS
    Builds scripts/migrations/phase0-up.sql and phase0-down.sql from the reviewed per-migration
    scripts (Phase 0 step 0.10, design 11.3).

.DESCRIPTION
    The bundles add the guards of design 11.3 around the unchanged scripts:
      - SQLCMD mode required (otherwise nothing runs), stop at the first error;
      - refuse a database that is not at the approved app's last migration, or that has
        migrations this bundle does not know;
      - at the end, refuse to report success unless the expected state is reached.
    The bundles are committed. CI rebuilds them and fails if they differ from the committed files.

    Usage (PowerShell 7):  pwsh scripts/migrations/build-phase0-bundle.ps1
#>
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot

$baseline = '20261003091001_AddVolunteerContactPersonDetails'
$app = @('20261013161410_M3_PlatformUniqueEmail', '20261013175341_M4_CompositeTenantKeys')
$platform = @('20261013080438_M1a_DataProtectionKeys', '20261013083152_M2_TenantMemberships', '20261013185539_M1b_ProviderSecurity')

# Order follows the migration ids (platform M1a and M2, app M3 and M4, platform M1b), then Hangfire.
$up = @('platform-M1a-up.idempotent.sql', 'platform-M2-up.idempotent.sql', 'app-M3-up.idempotent.sql',
        'app-M4-up.idempotent.sql', 'platform-M1b-up.idempotent.sql', 'hangfire-up.idempotent.sql')
$down = @('hangfire-down.idempotent.sql', 'platform-M1b-down.idempotent.sql', 'app-M4-down.idempotent.sql',
          'app-M3-down.idempotent.sql', 'platform-M2-down.idempotent.sql', 'platform-M1a-down.idempotent.sql')

function List($ids) { ($ids | ForEach-Object { "N'$_'" }) -join ', ' }

$guard = @"
-- Guard (design 11.3): SQLCMD mode, stop at the first error.
:on error exit
:setvar SqlCmdMode "ON"
GO
IF N'`$(SqlCmdMode)' <> N'ON'
BEGIN
    PRINT 'SQLCMD Mode is not enabled - nothing will be executed. Enable Query > SQLCMD Mode (SSMS) or use sqlcmd, and re-run.';
    SET NOEXEC ON;
END;
GO
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Guard: the right database (the approved app at its last migration) and nothing unknown.
-- (Own batch: on a database without the app, the history table does not exist, and naming it in
-- the same batch would fail with an unclear error before this check could run.)
IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]') IS NULL
    THROW 50020, 'Expected migration $baseline to be applied - wrong database?', 1;
GO
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'$baseline')
    THROW 50020, 'Expected migration $baseline to be applied - wrong database?', 1;
IF EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] > N'$baseline' AND [MigrationId] NOT IN ($(List $app)))
    THROW 50021, 'The database has app migrations this bundle does not know (a newer build was deployed?) - stopping.', 1;
-- (Platform history checks run as dynamic SQL: the table may not exist yet, and a missing table
-- named directly in a batch fails the whole batch before any IF can skip it.)
IF OBJECT_ID(N'[platform].[__EFMigrationsHistory]') IS NOT NULL
BEGIN
    DECLARE @unknown int;
    EXEC sp_executesql N'SELECT @n = COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE [MigrationId] NOT IN ($((List $platform) -replace "'", "''"))', N'@n int OUTPUT', @n = @unknown OUTPUT;
    IF @unknown > 0 THROW 50022, 'The database has platform migrations this bundle does not know - stopping.', 1;
END;
PRINT 'Guards passed: ' + DB_NAME() + ' on ' + @@SERVERNAME + '.';
GO

"@

function Body($files) {
    ($files | ForEach-Object {
        $text = [IO.File]::ReadAllText((Join-Path $dir $_)).TrimStart([char]0xFEFF).TrimEnd()
        "-- ===== $_ =====`nPRINT 'Running $_';`nGO`n$text`nGO`n"
    }) -join "`n"
}

$upFooter = @"
-- Expected state: every Phase 0 migration applied.
IF (SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] IN ($(List $app))) <> $($app.Count)
   OR (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE [MigrationId] IN ($(List $platform))) <> $($platform.Count)
   OR OBJECT_ID(N'[hangfire].[Schema]') IS NULL
    THROW 50023, 'Phase 0 schema is NOT complete - check the output above.', 1;
PRINT 'Phase 0 schema complete. Now run the verify scripts (scripts/migrations/README.md).';
GO
"@

$downFooter = @"
-- Expected state: back at the approved app's schema.
DECLARE @platform int = 0;
IF OBJECT_ID(N'[platform].[__EFMigrationsHistory]') IS NOT NULL
    EXEC sp_executesql N'SELECT @n = COUNT(*) FROM [platform].[__EFMigrationsHistory]', N'@n int OUTPUT', @n = @platform OUTPUT;
IF @platform > 0 THROW 50024, 'Phase 0 rollback is NOT complete: platform migrations remain - check the output above.', 1;
IF EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] IN ($(List $app)))
   OR SCHEMA_ID(N'hangfire') IS NOT NULL
    THROW 50024, 'Phase 0 rollback is NOT complete - check the output above.', 1;
PRINT 'Phase 0 rolled back: the database is at $baseline.';
GO
"@

$note = "-- GENERATED by scripts/migrations/build-phase0-bundle.ps1 - do not edit; edit the per-migration scripts and rebuild.`n"

$upText = $note + "-- Phase 0 schema, all at once: platform M1a, M2, app M3, M4, platform M1b, Hangfire storage. Idempotent.`n" +
          "-- Before: backup (design 11.2). Run: sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/phase0-up.sql`n`n" +
          $guard + (Body $up) + "`n" + $upFooter
$downText = $note + "-- Phase 0 rollback (design 11.6, R2): Hangfire storage, platform M1b, app M4, M3, platform M2, M1a. Idempotent.`n" +
            "-- Deletes the platform tables (memberships, provider users, audit log, jobs). Redeploy the previous build afterwards.`n`n" +
            $guard + (Body $down) + "`n" + $downFooter

# Same text on every machine: LF line endings, UTF-8 without BOM (git compares it line by line).
[IO.File]::WriteAllText((Join-Path $dir 'phase0-up.sql'), ($upText -replace "`r`n", "`n"), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $dir 'phase0-down.sql'), ($downText -replace "`r`n", "`n"), [Text.UTF8Encoding]::new($false))
Write-Output "Built phase0-up.sql and phase0-down.sql."
