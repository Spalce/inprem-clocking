-- Verification for app migration M4_CompositeTenantKeys. READ-ONLY.
-- After the up script: Applied = 1, AlternateKeys = 2, CompositeForeignKeys = 2, CrossedSessions = 0.
SET NOCOUNT ON;
SELECT
    (SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE MigrationId = N'20261013175341_M4_CompositeTenantKeys') AS Applied,
    (SELECT COUNT(*) FROM sys.key_constraints WHERE type = 'UQ' AND name IN (N'AK_Staffs_TenantId_StaffId', N'AK_Volunteers_TenantId_VolunteerId')) AS AlternateKeys,
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN (N'FK_ClockingsStaff_Staffs_TenantId_StafId', N'FK_Clockings_Volunteers_TenantId_VoluntId')) AS CompositeForeignKeys,
    (SELECT COUNT(*) FROM [Clockings] c JOIN [Volunteers] v ON v.VolunteerId = c.VoluntId WHERE c.TenantId <> v.TenantId)
      + (SELECT COUNT(*) FROM [ClockingsStaff] cs JOIN [Staffs] s ON s.StaffId = cs.StafId WHERE cs.TenantId <> s.TenantId) AS CrossedSessions;
GO
