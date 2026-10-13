BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [Clockings] DROP CONSTRAINT [FK_Clockings_Volunteers_TenantId_VoluntId];
END;
GO

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [ClockingsStaff] DROP CONSTRAINT [FK_ClockingsStaff_Staffs_TenantId_StafId];
END;
GO

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [Volunteers] DROP CONSTRAINT [AK_Volunteers_TenantId_VolunteerId];
END;
GO

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [Staffs] DROP CONSTRAINT [AK_Staffs_TenantId_StaffId];
END;
GO

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    CREATE INDEX [IX_ClockingsStaff_StafId] ON [ClockingsStaff] ([StafId]);
END;
GO

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    CREATE INDEX [IX_Clockings_VoluntId] ON [Clockings] ([VoluntId]);
END;
GO

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [Clockings] ADD CONSTRAINT [FK_Clockings_Volunteers_VoluntId] FOREIGN KEY ([VoluntId]) REFERENCES [Volunteers] ([VolunteerId]) ON DELETE CASCADE;
END;
GO

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    ALTER TABLE [ClockingsStaff] ADD CONSTRAINT [FK_ClockingsStaff_Staffs_StafId] FOREIGN KEY ([StafId]) REFERENCES [Staffs] ([StaffId]) ON DELETE CASCADE;
END;
GO

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013175341_M4_CompositeTenantKeys';
END;
GO

COMMIT;
GO

