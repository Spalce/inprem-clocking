BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    DECLARE @crossed int =
        (SELECT COUNT(*) FROM [Clockings] c JOIN [Volunteers] v ON v.VolunteerId = c.VoluntId WHERE c.TenantId <> v.TenantId)
      + (SELECT COUNT(*) FROM [ClockingsStaff] cs JOIN [Staffs] s ON s.StaffId = cs.StafId WHERE cs.TenantId <> s.TenantId);
    IF @crossed > 0
        THROW 50004, N'M4_CompositeTenantKeys stopped: some sessions belong to another organization than their person. Resolve them first (see scripts/inspection/04-data-quality.sql, result 4).', 1;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [Clockings] DROP CONSTRAINT [FK_Clockings_Volunteers_VoluntId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [ClockingsStaff] DROP CONSTRAINT [FK_ClockingsStaff_Staffs_StafId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    DROP INDEX [IX_ClockingsStaff_StafId] ON [ClockingsStaff];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    DROP INDEX [IX_Clockings_VoluntId] ON [Clockings];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [Volunteers] ADD CONSTRAINT [AK_Volunteers_TenantId_VolunteerId] UNIQUE ([TenantId], [VolunteerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [Staffs] ADD CONSTRAINT [AK_Staffs_TenantId_StaffId] UNIQUE ([TenantId], [StaffId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [Clockings] ADD CONSTRAINT [FK_Clockings_Volunteers_TenantId_VoluntId] FOREIGN KEY ([TenantId], [VoluntId]) REFERENCES [Volunteers] ([TenantId], [VolunteerId]) ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [ClockingsStaff] ADD CONSTRAINT [FK_ClockingsStaff_Staffs_TenantId_StafId] FOREIGN KEY ([TenantId], [StafId]) REFERENCES [Staffs] ([TenantId], [StaffId]) ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013175341_M4_CompositeTenantKeys', N'8.0.0');
END;
GO

COMMIT;
GO

