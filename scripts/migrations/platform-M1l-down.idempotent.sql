BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016063325_M1l_PaymentRefunds'
)
BEGIN
    DROP TABLE [platform].[PaymentRefunds];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016063325_M1l_PaymentRefunds'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016063325_M1l_PaymentRefunds';
END;
GO

COMMIT;
GO

