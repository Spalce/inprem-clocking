BEGIN TRANSACTION;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    DROP TABLE [platform].[BillingEvents];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    DROP TABLE [platform].[CheckoutSessions];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    DROP TABLE [platform].[Payments];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    DROP INDEX [IX_Subscriptions_ExternalSubscriptionId] ON [platform].[Subscriptions];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[Subscriptions]') AND [c].[name] = N'CancelAtPeriodEnd');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [platform].[Subscriptions] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [platform].[Subscriptions] DROP COLUMN [CancelAtPeriodEnd];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[Subscriptions]') AND [c].[name] = N'CurrentPeriodEndUtc');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [platform].[Subscriptions] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [platform].[Subscriptions] DROP COLUMN [CurrentPeriodEndUtc];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[Subscriptions]') AND [c].[name] = N'CurrentPeriodStartUtc');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [platform].[Subscriptions] DROP CONSTRAINT [' + @var2 + '];');
    ALTER TABLE [platform].[Subscriptions] DROP COLUMN [CurrentPeriodStartUtc];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    DECLARE @var3 sysname;
    SELECT @var3 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[Subscriptions]') AND [c].[name] = N'ExternalSubscriptionId');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [platform].[Subscriptions] DROP CONSTRAINT [' + @var3 + '];');
    ALTER TABLE [platform].[Subscriptions] DROP COLUMN [ExternalSubscriptionId];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    DECLARE @var4 sysname;
    SELECT @var4 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[Subscriptions]') AND [c].[name] = N'FreeAccessUntilUtc');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [platform].[Subscriptions] DROP CONSTRAINT [' + @var4 + '];');
    ALTER TABLE [platform].[Subscriptions] DROP COLUMN [FreeAccessUntilUtc];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    DECLARE @var5 sysname;
    SELECT @var5 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[Subscriptions]') AND [c].[name] = N'MaxPeopleOverride');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [platform].[Subscriptions] DROP CONSTRAINT [' + @var5 + '];');
    ALTER TABLE [platform].[Subscriptions] DROP COLUMN [MaxPeopleOverride];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    DECLARE @var6 sysname;
    SELECT @var6 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[Subscriptions]') AND [c].[name] = N'OutstandingBalance');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [platform].[Subscriptions] DROP CONSTRAINT [' + @var6 + '];');
    ALTER TABLE [platform].[Subscriptions] DROP COLUMN [OutstandingBalance];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    DECLARE @var7 sysname;
    SELECT @var7 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[Subscriptions]') AND [c].[name] = N'StatusChangedUtc');
    IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [platform].[Subscriptions] DROP CONSTRAINT [' + @var7 + '];');
    ALTER TABLE [platform].[Subscriptions] DROP COLUMN [StatusChangedUtc];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    DECLARE @var8 sysname;
    SELECT @var8 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[platform].[Plans]') AND [c].[name] = N'PayPalProductId');
    IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [platform].[Plans] DROP CONSTRAINT [' + @var8 + '];');
    ALTER TABLE [platform].[Plans] DROP COLUMN [PayPalProductId];
END;
GO

IF EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    DELETE FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling';
END;
GO

COMMIT;
GO

