BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161410_M3_PlatformUniqueEmail'
)
BEGIN
    DROP INDEX [EmailIndex] ON [AspNetUsers];
END;
GO

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161410_M3_PlatformUniqueEmail'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;
GO

IF EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161410_M3_PlatformUniqueEmail'
)
BEGIN
    DELETE FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013161410_M3_PlatformUniqueEmail';
END;
GO

COMMIT;
GO

