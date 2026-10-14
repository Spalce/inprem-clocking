BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014215921_M1e_SupportSessions'
)
BEGIN
    DROP TABLE [platform].[SupportSessions];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014215921_M1e_SupportSessions'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014215921_M1e_SupportSessions';
END;
GO

COMMIT;
GO

