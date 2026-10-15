BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015175927_M1h_PublicApi'
)
BEGIN
    DROP TABLE [platform].[ApiTokens];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015175927_M1h_PublicApi'
)
BEGIN
    DROP TABLE [platform].[IdempotencyRecords];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015175927_M1h_PublicApi'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015175927_M1h_PublicApi';
END;
GO

COMMIT;
GO

