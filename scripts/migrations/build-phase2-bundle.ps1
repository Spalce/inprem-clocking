<#
.SYNOPSIS
    Builds scripts/migrations/phase2-up.sql and phase2-down.sql from the reviewed per-migration
    scripts (Phase 2 exit, design 11.3).

.DESCRIPTION
    Same guards as the Phase 0 and Phase 1 bundles:
      - SQLCMD mode required (otherwise nothing runs), stop at the first error;
      - refuse a database that does not have the complete Phase 0 and Phase 1 schema, or that
        has migrations this bundle does not know;
      - at the end, refuse to report success unless the expected state is reached.
    Phase 2 has platform migrations only (M1f PayPal billing, M1g checkout index, M5 old billing rows).
    The bundles are committed. CI rebuilds them and fails if they differ from the committed files.

    Usage (PowerShell 7):  pwsh scripts/migrations/build-phase2-bundle.ps1
#>
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot

$baseline = '20261003091001_AddVolunteerContactPersonDetails'
$app = @('20261013161410_M3_PlatformUniqueEmail', '20261013175341_M4_CompositeTenantKeys')
$phase0Platform = @('20261013080438_M1a_DataProtectionKeys', '20261013083152_M2_TenantMemberships', '20261013185539_M1b_ProviderSecurity')
$phase1Platform = @('20261014204227_M1c_Plans', '20261014210206_M1d_Organizations', '20261014215921_M1e_SupportSessions')
$phase2 = @('20261015074938_M1f_PayPalBilling', '20261015094743_M1g_CheckoutPlanChanges', '20261015170549_M5_LegacyBillingMoved')
$before = $phase0Platform + $phase1Platform

$up = @('platform-M1f-up.idempotent.sql', 'platform-M1g-up.idempotent.sql', 'platform-M5-up.idempotent.sql')
$down = @('platform-M5-down.idempotent.sql', 'platform-M1g-down.idempotent.sql', 'platform-M1f-down.idempotent.sql')

function List($ids) { ($ids | ForEach-Object { "N'$_'" }) -join ', ' }
function Quoted($ids) { (List $ids) -replace "'", "''" }

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

-- Guard: the right database (the complete Phase 0 and Phase 1 schema) and nothing unknown.
-- (Own batches and dynamic SQL: a missing history table named directly in a batch fails the
-- whole batch with an unclear error before any check could run.)
IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]') IS NULL OR OBJECT_ID(N'[platform].[__EFMigrationsHistory]') IS NULL
    THROW 50040, 'The Phase 0 schema is not there (run phase0-up.sql and phase1-up.sql first) - wrong database?', 1;
GO
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'$baseline')
   OR (SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] IN ($(List $app))) <> $($app.Count)
    THROW 50040, 'The Phase 0 schema is not complete (run phase0-up.sql first) - wrong database?', 1;
IF EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] > N'$baseline' AND [MigrationId] NOT IN ($(List $app)))
    THROW 50041, 'The database has app migrations this bundle does not know (a newer build was deployed?) - stopping.', 1;
DECLARE @before int, @unknown int;
EXEC sp_executesql N'SELECT @n = COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE [MigrationId] IN ($(Quoted $before))', N'@n int OUTPUT', @n = @before OUTPUT;
IF @before <> $($before.Count) OR SCHEMA_ID(N'hangfire') IS NULL
    THROW 50040, 'The Phase 0 and Phase 1 schema is not complete (run phase0-up.sql and phase1-up.sql first).', 1;
EXEC sp_executesql N'SELECT @n = COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE [MigrationId] NOT IN ($(Quoted ($before + $phase2)))', N'@n int OUTPUT', @n = @unknown OUTPUT;
IF @unknown > 0 THROW 50042, 'The database has platform migrations this bundle does not know - stopping.', 1;
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
-- Expected state: every Phase 2 migration applied.
DECLARE @done int;
EXEC sp_executesql N'SELECT @n = COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE [MigrationId] IN ($(Quoted $phase2))', N'@n int OUTPUT', @n = @done OUTPUT;
IF @done <> $($phase2.Count) THROW 50043, 'Phase 2 schema is NOT complete - check the output above.', 1;
PRINT 'Phase 2 schema complete. Now run the verify scripts (scripts/migrations/README.md).';
GO
"@

$downFooter = @"
-- Expected state: back at the Phase 1 schema.
DECLARE @left int, @kept int;
EXEC sp_executesql N'SELECT @n = COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE [MigrationId] IN ($(Quoted $phase2))', N'@n int OUTPUT', @n = @left OUTPUT;
EXEC sp_executesql N'SELECT @n = COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE [MigrationId] IN ($(Quoted $before))', N'@n int OUTPUT', @n = @kept OUTPUT;
IF @left > 0 OR @kept <> $($before.Count) THROW 50044, 'Phase 2 rollback is NOT complete - check the output above.', 1;
PRINT 'Phase 2 rolled back: the database has the Phase 1 schema.';
GO
"@

$note = "-- GENERATED by scripts/migrations/build-phase2-bundle.ps1 - do not edit; edit the per-migration scripts and rebuild.`n"

$upText = $note + "-- Phase 2 schema, all at once: platform M1f (PayPal billing), M1g (checkout index), M5 (old billing rows moved). Idempotent. Needs the Phase 0 and Phase 1 schema.`n" +
          "-- Before: backup (design 11.2). Run: sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/phase2-up.sql`n`n" +
          $guard + (Body $up) + "`n" + $upFooter
$downText = $note + "-- Phase 2 rollback (design 11.6): platform M5, M1g, M1f. Idempotent.`n" +
            "-- Deletes checkouts, payments, PayPal events and the PayPal fields of subscriptions; moved old billing rows go back (the audit log stays). Redeploy the previous build afterwards.`n`n" +
            $guard + (Body $down) + "`n" + $downFooter

# Same text on every machine: LF line endings, UTF-8 without BOM (git compares it line by line).
[IO.File]::WriteAllText((Join-Path $dir 'phase2-up.sql'), ($upText -replace "`r`n", "`n"), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $dir 'phase2-down.sql'), ($downText -replace "`r`n", "`n"), [Text.UTF8Encoding]::new($false))
Write-Output "Built phase2-up.sql and phase2-down.sql."
