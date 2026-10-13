-- 02 - EF Core migration history (ApplicationDbContext, dbo) vs the migrations in this branch.
-- The approved app (fix/login-admin-only, 1061ea8) had the first 15; Phase 0 adds M3 and M4.
-- Platform migrations (PlatformDbContext) are in platform.__EFMigrationsHistory: see scripts/migrations.
-- READ-ONLY. Run against a RESTORED LOCAL COPY only (see README.md).
-- Expected: result 2 and 3 are empty, result 4 shows LastApplied = LastExpected.
:on error exit
SET NOCOUNT ON;
IF '$(ConfirmLocalCopy)' <> 'YES'
BEGIN
    RAISERROR('Refusing to run: pass -v ConfirmLocalCopy=YES to confirm this is a restored local copy, never the hosted database.', 16, 1);
    SET NOEXEC ON;
END
GO

DECLARE @Expected TABLE (MigrationId nvarchar(150) PRIMARY KEY);
INSERT INTO @Expected (MigrationId) VALUES
    (N'20260909115551_InitialCreate'),
    (N'20260925155246_AddVolunteerCategoryFields'),
    (N'20260925155854_UseAppRoleForIdentity'),
    (N'20260925172433_AddClockDateUniquePerDay'),
    (N'20260928094128_AddUniqueEmailIndexes'),
    (N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'),
    (N'20260929053332_MultiTenancy_Phase1a_BackfillTenantData'),
    (N'20260929053552_MultiTenancy_Phase1b_TightenSchema'),
    (N'20260929124900_MultiTenancy_Phase5_AddTenantProfileFields'),
    (N'20261001111045_MultiTenancy_Phase6a_AddBillingTables'),
    (N'20261001111135_MultiTenancy_Phase6b_SeedSubscriptions'),
    (N'20261001133327_AddInvoicePeriodUniqueIndex'),
    (N'20261001135332_TenantScopedAdminUsernames'),
    (N'20261002182520_RemoveLogoutSettingsFeature'),
    (N'20261003091001_AddVolunteerContactPersonDetails'),
    (N'20261013161410_M3_PlatformUniqueEmail'),
    (N'20261013175341_M4_CompositeTenantKeys');

-- 1. Applied migrations, in order.
SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory ORDER BY MigrationId;

-- 2. On the branch but not applied to this database.
SELECT e.MigrationId AS MissingFromDatabase
FROM @Expected e
WHERE NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory h WHERE h.MigrationId = e.MigrationId)
ORDER BY e.MigrationId;

-- 3. Applied to this database but not on the branch.
SELECT h.MigrationId AS NotOnBranch
FROM __EFMigrationsHistory h
WHERE NOT EXISTS (SELECT 1 FROM @Expected e WHERE e.MigrationId = h.MigrationId)
ORDER BY h.MigrationId;

-- 4. Summary.
SELECT
    (SELECT MAX(MigrationId) FROM __EFMigrationsHistory) AS LastApplied,
    (SELECT MAX(MigrationId) FROM @Expected)             AS LastExpected,
    (SELECT COUNT(*) FROM __EFMigrationsHistory)         AS AppliedCount,
    (SELECT COUNT(*) FROM @Expected)                     AS ExpectedCount;
GO
