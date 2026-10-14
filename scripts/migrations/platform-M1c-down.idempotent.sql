BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014204227_M1c_Plans'
)
BEGIN
    DROP TABLE [platform].[PlanPrices];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014204227_M1c_Plans'
)
BEGIN
    DROP TABLE [platform].[Plans];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014204227_M1c_Plans'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014204227_M1c_Plans';
END;
GO

COMMIT;
GO

