<#
.SYNOPSIS
    Builds scripts/migrations/phase6-up.sql and phase6-down.sql from the reviewed per-migration
    scripts (Phase 6 exit, design 11.3).

.DESCRIPTION
    Same guards as the earlier bundles:
      - SQLCMD mode required (otherwise nothing runs), stop at the first error;
      - refuse a database that does not have the complete Phase 0 to 5 schema, or that has
        migrations this bundle does not know;
      - at the end, refuse to report success unless the expected state is reached.
    Phase 6 has one platform migration (M1p: the organization's staff and volunteer alert settings).
    The bundles are committed. CI rebuilds them and fails if they differ from the committed files.

    Usage (PowerShell 7):  pwsh scripts/migrations/build-phase6-bundle.ps1
#>
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot

$baseline = '20261003091001_AddVolunteerContactPersonDetails'
$app = @('20261013161410_M3_PlatformUniqueEmail', '20261013175341_M4_CompositeTenantKeys')
$phase0Platform = @('20261013080438_M1a_DataProtectionKeys', '20261013083152_M2_TenantMemberships', '20261013185539_M1b_ProviderSecurity')
$phase1Platform = @('20261014204227_M1c_Plans', '20261014210206_M1d_Organizations', '20261014215921_M1e_SupportSessions')
$phase2Platform = @('20261015074938_M1f_PayPalBilling', '20261015094743_M1g_CheckoutPlanChanges', '20261015170549_M5_LegacyBillingMoved')
$phase3Platform = @('20261015175927_M1h_PublicApi', '20261015194018_M1i_Signups', '20261015203907_M1j_BillingAccessCodes')
$phase4Platform = @('20261016022134_M1k_Invoices', '20261016063325_M1l_PaymentRefunds')
$phase5Platform = @('20261019224539_M1m_Notifications', '20261019233541_M1n_SmsSignIn', '20261020001530_M1o_ProviderAlertPreferences')
$phase6 = @('20261020010938_M1p_StaffAlertSettings')
$before = $phase0Platform + $phase1Platform + $phase2Platform + $phase3Platform + $phase4Platform + $phase5Platform

$up = @('platform-M1p-up.idempotent.sql')
$down = @('platform-M1p-down.idempotent.sql')

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

-- Guard: the right database (the complete Phase 0 to 5 schema) and nothing unknown.
-- (Own batches and dynamic SQL: a missing history table named directly in a batch fails the
-- whole batch with an unclear error before any check could run.)
IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]') IS NULL OR OBJECT_ID(N'[platform].[__EFMigrationsHistory]') IS NULL
    THROW 50080, 'The Phase 0 schema is not there (run the phase0 to phase5 bundles first) - wrong database?', 1;
GO
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'$baseline')
   OR (SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] IN ($(List $app))) <> $($app.Count)
    THROW 50080, 'The Phase 0 schema is not complete (run phase0-up.sql first) - wrong database?', 1;
IF EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] > N'$baseline' AND [MigrationId] NOT IN ($(List $app)))
    THROW 50081, 'The database has app migrations this bundle does not know (a newer build was deployed?) - stopping.', 1;
DECLARE @before int, @unknown int;
EXEC sp_executesql N'SELECT @n = COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE [MigrationId] IN ($(Quoted $before))', N'@n int OUTPUT', @n = @before OUTPUT;
IF @before <> $($before.Count) OR SCHEMA_ID(N'hangfire') IS NULL
    THROW 50080, 'The Phase 0 to 5 schema is not complete (run the phase0 to phase5 bundles first).', 1;
EXEC sp_executesql N'SELECT @n = COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE [MigrationId] NOT IN ($(Quoted ($before + $phase6)))', N'@n int OUTPUT', @n = @unknown OUTPUT;
IF @unknown > 0 THROW 50082, 'The database has platform migrations this bundle does not know - stopping.', 1;
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
-- Expected state: every Phase 6 migration applied.
DECLARE @done int;
EXEC sp_executesql N'SELECT @n = COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE [MigrationId] IN ($(Quoted $phase6))', N'@n int OUTPUT', @n = @done OUTPUT;
IF @done <> $($phase6.Count) THROW 50083, 'Phase 6 schema is NOT complete - check the output above.', 1;
PRINT 'Phase 6 schema complete. Now run the verify scripts (scripts/migrations/README.md).';
GO
"@

$downFooter = @"
-- Expected state: back at the Phase 5 schema.
DECLARE @left int, @kept int;
EXEC sp_executesql N'SELECT @n = COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE [MigrationId] IN ($(Quoted $phase6))', N'@n int OUTPUT', @n = @left OUTPUT;
EXEC sp_executesql N'SELECT @n = COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE [MigrationId] IN ($(Quoted $before))', N'@n int OUTPUT', @n = @kept OUTPUT;
IF @left > 0 OR @kept <> $($before.Count) THROW 50084, 'Phase 6 rollback is NOT complete - check the output above.', 1;
PRINT 'Phase 6 rolled back: the database has the Phase 5 schema.';
GO
"@

$note = "-- GENERATED by scripts/migrations/build-phase6-bundle.ps1 - do not edit; edit the per-migration scripts and rebuild.`n"

$upText = $note + "-- Phase 6 schema: platform M1p (staff and volunteer alert settings). Idempotent. Needs the Phase 0 to 5 schema.`n" +
          "-- Before: backup (design 11.2). Run: sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/phase6-up.sql`n`n" +
          $guard + (Body $up) + "`n" + $upFooter
$downText = $note + "-- Phase 6 rollback (design 11.6): platform M1p. Idempotent.`n" +
            "-- Removes the four staff alert columns: every organization's staff alert choices are lost (consent records and the message log stay). Redeploy the previous build afterwards.`n`n" +
            $guard + (Body $down) + "`n" + $downFooter

# Same text on every machine: LF line endings, UTF-8 without BOM (git compares it line by line).
[IO.File]::WriteAllText((Join-Path $dir 'phase6-up.sql'), ($upText -replace "`r`n", "`n"), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $dir 'phase6-down.sql'), ($downText -replace "`r`n", "`n"), [Text.UTF8Encoding]::new($false))
Write-Output "Built phase6-up.sql and phase6-down.sql."
