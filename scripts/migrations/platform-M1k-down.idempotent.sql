BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    ALTER TABLE [platform].[Payments] DROP CONSTRAINT [FK_Payments_Invoices_InvoiceId];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    DROP TABLE [platform].[InvoiceLines];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    DROP TABLE [platform].[Invoices];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    DROP INDEX [IX_Payments_InvoiceId] ON [platform].[Payments];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[Payments]') AND [c].[name] = N'InvoiceId');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [platform].[Payments] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [platform].[Payments] DROP COLUMN [InvoiceId];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[Payments]') AND [c].[name] = N'Method');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [platform].[Payments] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [platform].[Payments] DROP COLUMN [Method];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    DROP SEQUENCE [platform].[InvoiceNumbers];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices';
END;
GO

COMMIT;
GO

