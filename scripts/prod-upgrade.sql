/*
 * Production upgrade: AddUniqueEmailIndexes -> AddVolunteerContactPersonDetails (10 migrations).
 *
 * Generated with (from InpremClockingApp/):
 *   dotnet ef migrations script 20260928094128_AddUniqueEmailIndexes --idempotent -o ../scripts/prod-upgrade.sql
 * then hand-edited in three places, each marked "PROD-UPGRADE ADDITION":
 *   1. this header: stop at the first error, and refuse to run against a database that
 *      isn't at AddUniqueEmailIndexes (i.e. the wrong database);
 *   2. after Phase1a: verify exactly one tenant exists and every existing row/user was
 *      assigned to it, before Phase1b makes TenantId NOT NULL;
 *   3. before RemoveLogoutSettingsFeature: copy [Setting] to [Setting_Archive] so the
 *      only DROP in this script loses no data.
 *
 * Existing Staff/Volunteer/Clocking/user rows are never deleted. Phase1a creates the one
 * tenant ("Inprem Holistic Community Resource Center"), reads its real generated Id via
 * SCOPE_IDENTITY() (no assumption that it is 1) and assigns every existing row to it.
 *
 * HOW TO RUN:
 *   - Take a backup first (SmarterASP control panel -> Databases -> Backup).
 *   - SSMS: Query menu -> SQLCMD Mode MUST be ON. Without it, the guard below switches the
 *     whole script to compile-only (SET NOEXEC ON) and nothing is executed.
 *   - or: sqlcmd -S <server> -d <database> -U <user> -P <password> -b -i scripts\prod-upgrade.sql
 *   - Rehearse against a restored copy of the backup before running it on production.
 *
 * On any error the current migration's transaction rolls back and the script stops.
 * Migrations completed before it stay applied and recorded, so after fixing the cause the
 * script can simply be re-run - each step is skipped if already in __EFMigrationsHistory.
 */

-- PROD-UPGRADE ADDITION 1: stop on first error; refuse to run outside SQLCMD mode.
:on error exit
:setvar SqlCmdMode "ON"
GO
IF N'$(SqlCmdMode)' <> N'ON'
BEGIN
    PRINT 'SQLCMD Mode is not enabled - nothing will be executed. Enable Query > SQLCMD Mode and re-run.';
    SET NOEXEC ON;
END;
GO

SET XACT_ABORT ON;
-- Required by the filtered indexes on AspNetUsers/AspNetRoles (and Phase1b's new ones).
-- SSMS defaults these ON, but sqlcmd defaults QUOTED_IDENTIFIER OFF, which makes Phase1a's
-- UPDATE [AspNetUsers] fail.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260928094128_AddUniqueEmailIndexes')
    THROW 50001, 'Expected migration 20260928094128_AddUniqueEmailIndexes to already be applied - wrong database?', 1;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [Volunteers] ADD [TenantId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [Staffs] ADD [TenantId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [Setting] ADD [TenantId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [ClockingsStaff] ADD [TenantId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [Clockings] ADD [TenantId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [TenantId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    CREATE TABLE [Tenants] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [TimeZoneId] nvarchar(100) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    CREATE INDEX [IX_Volunteers_TenantId] ON [Volunteers] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    CREATE INDEX [IX_Staffs_TenantId] ON [Staffs] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    CREATE INDEX [IX_Setting_TenantId] ON [Setting] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    CREATE INDEX [IX_ClockingsStaff_TenantId] ON [ClockingsStaff] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    CREATE INDEX [IX_Clockings_TenantId] ON [Clockings] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    CREATE INDEX [IX_AspNetUsers_TenantId] ON [AspNetUsers] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD CONSTRAINT [FK_AspNetUsers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [Clockings] ADD CONSTRAINT [FK_Clockings_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [ClockingsStaff] ADD CONSTRAINT [FK_ClockingsStaff_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [Setting] ADD CONSTRAINT [FK_Setting_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [Staffs] ADD CONSTRAINT [FK_Staffs_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    ALTER TABLE [Volunteers] ADD CONSTRAINT [FK_Volunteers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260929052932_MultiTenancy_Phase0_AddTenantAndNullableTenantId', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053332_MultiTenancy_Phase1a_BackfillTenantData'
)
BEGIN
                    INSERT INTO [Tenants] ([Name], [TimeZoneId], [IsActive], [CreatedAt])
                    VALUES (N'Inprem Holistic Community Resource Center', N'America/New_York', 1, SYSUTCDATETIME());
                    DECLARE @TenantId INT = SCOPE_IDENTITY();
                    UPDATE [Staffs] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
                    UPDATE [Volunteers] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
                    UPDATE [ClockingsStaff] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
                    UPDATE [Clockings] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
                    UPDATE [Setting] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
                    UPDATE [AspNetUsers] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053332_MultiTenancy_Phase1a_BackfillTenantData'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260929053332_MultiTenancy_Phase1a_BackfillTenantData', N'8.0.0');
END;
GO

COMMIT;
GO

-- PROD-UPGRADE ADDITION 2: before Phase1b makes TenantId NOT NULL, confirm Phase1a created
-- exactly one tenant and assigned every existing row and user to it. (Single-tenant deploy:
-- no SuperAdmin accounts exist yet, so every AspNetUsers row must have a tenant too.)
IF (SELECT COUNT(*) FROM [Tenants]) <> 1
    THROW 50002, 'Expected exactly one row in [Tenants] after Phase1a.', 1;
IF EXISTS (SELECT 1 FROM [Staffs] WHERE [TenantId] IS NULL)
   OR EXISTS (SELECT 1 FROM [Volunteers] WHERE [TenantId] IS NULL)
   OR EXISTS (SELECT 1 FROM [ClockingsStaff] WHERE [TenantId] IS NULL)
   OR EXISTS (SELECT 1 FROM [Clockings] WHERE [TenantId] IS NULL)
   OR EXISTS (SELECT 1 FROM [AspNetUsers] WHERE [TenantId] IS NULL)
    THROW 50003, 'Some existing rows were not assigned a TenantId by Phase1a.', 1;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_Volunteers_EmailAddress] ON [Volunteers];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_Volunteers_TenantId] ON [Volunteers];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_Staffs_EmailAddress] ON [Staffs];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_Staffs_TenantId] ON [Staffs];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_Setting_TenantId] ON [Setting];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_ClockingsStaff_StafId_ClockDate] ON [ClockingsStaff];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_ClockingsStaff_TenantId] ON [ClockingsStaff];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_Clockings_TenantId] ON [Clockings];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DROP INDEX [IX_Clockings_VoluntId_ClockDate] ON [Clockings];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Volunteers]') AND [c].[name] = N'TenantId');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Volunteers] DROP CONSTRAINT [' + @var0 + '];');
    EXEC(N'UPDATE [Volunteers] SET [TenantId] = 0 WHERE [TenantId] IS NULL');
    ALTER TABLE [Volunteers] ALTER COLUMN [TenantId] int NOT NULL;
    ALTER TABLE [Volunteers] ADD DEFAULT 0 FOR [TenantId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Staffs]') AND [c].[name] = N'TenantId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Staffs] DROP CONSTRAINT [' + @var1 + '];');
    EXEC(N'UPDATE [Staffs] SET [TenantId] = 0 WHERE [TenantId] IS NULL');
    ALTER TABLE [Staffs] ALTER COLUMN [TenantId] int NOT NULL;
    ALTER TABLE [Staffs] ADD DEFAULT 0 FOR [TenantId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Setting]') AND [c].[name] = N'TenantId');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Setting] DROP CONSTRAINT [' + @var2 + '];');
    EXEC(N'UPDATE [Setting] SET [TenantId] = 0 WHERE [TenantId] IS NULL');
    ALTER TABLE [Setting] ALTER COLUMN [TenantId] int NOT NULL;
    ALTER TABLE [Setting] ADD DEFAULT 0 FOR [TenantId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DECLARE @var3 sysname;
    SELECT @var3 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ClockingsStaff]') AND [c].[name] = N'TenantId');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [ClockingsStaff] DROP CONSTRAINT [' + @var3 + '];');
    EXEC(N'UPDATE [ClockingsStaff] SET [TenantId] = 0 WHERE [TenantId] IS NULL');
    ALTER TABLE [ClockingsStaff] ALTER COLUMN [TenantId] int NOT NULL;
    ALTER TABLE [ClockingsStaff] ADD DEFAULT 0 FOR [TenantId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    DECLARE @var4 sysname;
    SELECT @var4 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Clockings]') AND [c].[name] = N'TenantId');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [Clockings] DROP CONSTRAINT [' + @var4 + '];');
    EXEC(N'UPDATE [Clockings] SET [TenantId] = 0 WHERE [TenantId] IS NULL');
    ALTER TABLE [Clockings] ALTER COLUMN [TenantId] int NOT NULL;
    ALTER TABLE [Clockings] ADD DEFAULT 0 FOR [TenantId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Volunteers_TenantId_EmailAddress] ON [Volunteers] ([TenantId], [EmailAddress]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Staffs_TenantId_EmailAddress] ON [Staffs] ([TenantId], [EmailAddress]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Setting_TenantId] ON [Setting] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    CREATE INDEX [IX_ClockingsStaff_StafId] ON [ClockingsStaff] ([StafId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ClockingsStaff_TenantId_StafId_ClockDate] ON [ClockingsStaff] ([TenantId], [StafId], [ClockDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Clockings_TenantId_VoluntId_ClockDate] ON [Clockings] ([TenantId], [VoluntId], [ClockDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    CREATE INDEX [IX_Clockings_VoluntId] ON [Clockings] ([VoluntId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929053552_MultiTenancy_Phase1b_TightenSchema'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260929053552_MultiTenancy_Phase1b_TightenSchema', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929124900_MultiTenancy_Phase5_AddTenantProfileFields'
)
BEGIN
    ALTER TABLE [Tenants] ADD [Address] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929124900_MultiTenancy_Phase5_AddTenantProfileFields'
)
BEGIN
    ALTER TABLE [Tenants] ADD [ContactInfo] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929124900_MultiTenancy_Phase5_AddTenantProfileFields'
)
BEGIN
                    UPDATE Tenants
                    SET Address = N'5757 Karl Road, Columbus, OH 43229',
                        ContactInfo = N'614-516-1812 | Inpremcommunitycenter@yahoo.com'
                    WHERE Name = N'Inprem Holistic Community Resource Center';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929124900_MultiTenancy_Phase5_AddTenantProfileFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260929124900_MultiTenancy_Phase5_AddTenantProfileFields', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001111045_MultiTenancy_Phase6a_AddBillingTables'
)
BEGIN
    CREATE TABLE [Subscriptions] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] int NOT NULL,
        [Amount] decimal(10,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [BillingCycle] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CurrentPeriodStart] datetime2 NOT NULL,
        [CurrentPeriodEnd] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [PaymentGateway] nvarchar(30) NULL,
        [ExternalSubscriptionId] nvarchar(100) NULL,
        CONSTRAINT [PK_Subscriptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Subscriptions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001111045_MultiTenancy_Phase6a_AddBillingTables'
)
BEGIN
    CREATE TABLE [Invoices] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] int NOT NULL,
        [SubscriptionId] int NOT NULL,
        [InvoiceNumber] nvarchar(30) NOT NULL,
        [PeriodStart] datetime2 NOT NULL,
        [PeriodEnd] datetime2 NOT NULL,
        [Amount] decimal(10,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [IssuedDate] datetime2 NOT NULL,
        [DueDate] datetime2 NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [PaidDate] datetime2 NULL,
        [PaymentMethod] nvarchar(100) NULL,
        [PaymentReference] nvarchar(100) NULL,
        [Notes] nvarchar(500) NULL,
        CONSTRAINT [PK_Invoices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Invoices_Subscriptions_SubscriptionId] FOREIGN KEY ([SubscriptionId]) REFERENCES [Subscriptions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Invoices_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001111045_MultiTenancy_Phase6a_AddBillingTables'
)
BEGIN
    CREATE INDEX [IX_Invoices_SubscriptionId] ON [Invoices] ([SubscriptionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001111045_MultiTenancy_Phase6a_AddBillingTables'
)
BEGIN
    CREATE INDEX [IX_Invoices_TenantId] ON [Invoices] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001111045_MultiTenancy_Phase6a_AddBillingTables'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Subscriptions_TenantId] ON [Subscriptions] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001111045_MultiTenancy_Phase6a_AddBillingTables'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001111045_MultiTenancy_Phase6a_AddBillingTables', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001111135_MultiTenancy_Phase6b_SeedSubscriptions'
)
BEGIN
                    INSERT INTO [Subscriptions] ([TenantId], [Amount], [Currency], [BillingCycle], [Status], [CurrentPeriodStart], [CurrentPeriodEnd], [CreatedAt], [PaymentGateway], [ExternalSubscriptionId])
                    SELECT t.[Id], 0, N'USD', N'Monthly', N'Active', SYSUTCDATETIME(), DATEADD(YEAR, 1, SYSUTCDATETIME()), SYSUTCDATETIME(), NULL, NULL
                    FROM [Tenants] t
                    WHERE NOT EXISTS (SELECT 1 FROM [Subscriptions] s WHERE s.[TenantId] = t.[Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001111135_MultiTenancy_Phase6b_SeedSubscriptions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001111135_MultiTenancy_Phase6b_SeedSubscriptions', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001133327_AddInvoicePeriodUniqueIndex'
)
BEGIN
    DROP INDEX [IX_Invoices_SubscriptionId] ON [Invoices];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001133327_AddInvoicePeriodUniqueIndex'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [ActiveInvoicePerPeriodIndex] ON [Invoices] ([SubscriptionId], [PeriodStart]) WHERE [Status] <> ''Void''');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001133327_AddInvoicePeriodUniqueIndex'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001133327_AddInvoicePeriodUniqueIndex', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001135332_TenantScopedAdminUsernames'
)
BEGIN
    DROP INDEX [IX_AspNetUsers_TenantId] ON [AspNetUsers];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001135332_TenantScopedAdminUsernames'
)
BEGIN
    DROP INDEX [UserNameIndex] ON [AspNetUsers];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001135332_TenantScopedAdminUsernames'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [SuperAdminNormalizedUserNameIndex] ON [AspNetUsers] ([TenantId], [NormalizedUserName]) WHERE [TenantId] IS NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001135332_TenantScopedAdminUsernames'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [TenantNormalizedUserNameIndex] ON [AspNetUsers] ([TenantId], [NormalizedUserName]) WHERE [TenantId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001135332_TenantScopedAdminUsernames'
)
BEGIN
    CREATE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261001135332_TenantScopedAdminUsernames'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261001135332_TenantScopedAdminUsernames', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002182520_RemoveLogoutSettingsFeature'
)
BEGIN
    -- PROD-UPGRADE ADDITION 3: keep a copy of the old logout-timer settings before the drop.
    IF OBJECT_ID(N'[Setting_Archive]') IS NULL
        EXEC(N'SELECT * INTO [Setting_Archive] FROM [Setting];');
    DROP TABLE [Setting];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002182520_RemoveLogoutSettingsFeature'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002182520_RemoveLogoutSettingsFeature', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003091001_AddVolunteerContactPersonDetails'
)
BEGIN
    ALTER TABLE [Volunteers] ADD [ContactPersonEmail] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003091001_AddVolunteerContactPersonDetails'
)
BEGIN
    ALTER TABLE [Volunteers] ADD [ContactPersonPhone] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003091001_AddVolunteerContactPersonDetails'
)
BEGIN
    ALTER TABLE [Volunteers] ADD [ContactPersonPosition] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261003091001_AddVolunteerContactPersonDetails'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261003091001_AddVolunteerContactPersonDetails', N'8.0.0');
END;
GO

COMMIT;
GO

