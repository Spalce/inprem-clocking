BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    ALTER TABLE [platform].[Subscriptions] ADD [CancelAtPeriodEnd] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    ALTER TABLE [platform].[Subscriptions] ADD [CurrentPeriodEndUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    ALTER TABLE [platform].[Subscriptions] ADD [CurrentPeriodStartUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    ALTER TABLE [platform].[Subscriptions] ADD [ExternalSubscriptionId] varchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    ALTER TABLE [platform].[Subscriptions] ADD [FreeAccessUntilUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    ALTER TABLE [platform].[Subscriptions] ADD [MaxPeopleOverride] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    ALTER TABLE [platform].[Subscriptions] ADD [OutstandingBalance] decimal(10,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    ALTER TABLE [platform].[Subscriptions] ADD [StatusChangedUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    ALTER TABLE [platform].[Plans] ADD [PayPalProductId] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    CREATE TABLE [platform].[BillingEvents] (
        [ExternalEventId] varchar(100) NOT NULL,
        [EventType] varchar(100) NOT NULL,
        [ResourceType] varchar(50) NULL,
        [ResourceId] varchar(100) NULL,
        [RawJson] nvarchar(max) NOT NULL,
        [ReceivedUtc] datetime2 NOT NULL,
        [ProcessedUtc] datetime2 NULL,
        [Attempts] int NOT NULL,
        [Error] nvarchar(2000) NULL,
        CONSTRAINT [PK_BillingEvents] PRIMARY KEY ([ExternalEventId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    CREATE TABLE [platform].[CheckoutSessions] (
        [Id] uniqueidentifier NOT NULL,
        [Reference] varchar(40) NOT NULL,
        [TenantId] int NULL,
        [SignupId] uniqueidentifier NULL,
        [Purpose] nvarchar(20) NOT NULL,
        [PlanPriceId] uniqueidentifier NOT NULL,
        [PayPalPlanId] varchar(50) NULL,
        [Status] nvarchar(20) NOT NULL,
        [ExternalSubscriptionId] varchar(50) NULL,
        [ApprovalUrl] nvarchar(2000) NULL,
        [ReturnUrl] nvarchar(2000) NOT NULL,
        [CancelUrl] nvarchar(2000) NOT NULL,
        [IdempotencyKey] nvarchar(100) NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [ExpiresUtc] datetime2 NOT NULL,
        [CompletedUtc] datetime2 NULL,
        [FailureReason] nvarchar(500) NULL,
        [CreatedBy] nvarchar(300) NOT NULL,
        CONSTRAINT [PK_CheckoutSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CheckoutSessions_PlanPrices_PlanPriceId] FOREIGN KEY ([PlanPriceId]) REFERENCES [platform].[PlanPrices] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    CREATE TABLE [platform].[Payments] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] int NOT NULL,
        [SubscriptionId] uniqueidentifier NULL,
        [Source] nvarchar(20) NOT NULL,
        [ExternalId] nvarchar(100) NULL,
        [Amount] decimal(10,2) NOT NULL,
        [Currency] char(3) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [PaidUtc] datetime2 NOT NULL,
        [RefundedUtc] datetime2 NULL,
        [RefundedAmount] decimal(10,2) NOT NULL,
        [RecordedBy] nvarchar(300) NOT NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Payments_Subscriptions_SubscriptionId] FOREIGN KEY ([SubscriptionId]) REFERENCES [platform].[Subscriptions] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Subscriptions_ExternalSubscriptionId] ON [platform].[Subscriptions] ([ExternalSubscriptionId]) WHERE [ExternalSubscriptionId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    CREATE INDEX [IX_BillingEvents_ReceivedUtc] ON [platform].[BillingEvents] ([ReceivedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    CREATE INDEX [IX_BillingEvents_ResourceId] ON [platform].[BillingEvents] ([ResourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CheckoutSessions_ExternalSubscriptionId] ON [platform].[CheckoutSessions] ([ExternalSubscriptionId]) WHERE [ExternalSubscriptionId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CheckoutSessions_IdempotencyKey] ON [platform].[CheckoutSessions] ([IdempotencyKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    CREATE INDEX [IX_CheckoutSessions_PlanPriceId] ON [platform].[CheckoutSessions] ([PlanPriceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CheckoutSessions_Reference] ON [platform].[CheckoutSessions] ([Reference]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    CREATE INDEX [IX_CheckoutSessions_Status_ExpiresUtc] ON [platform].[CheckoutSessions] ([Status], [ExpiresUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    CREATE INDEX [IX_CheckoutSessions_TenantId] ON [platform].[CheckoutSessions] ([TenantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Payments_Source_ExternalId] ON [platform].[Payments] ([Source], [ExternalId]) WHERE [ExternalId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    CREATE INDEX [IX_Payments_SubscriptionId] ON [platform].[Payments] ([SubscriptionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    CREATE INDEX [IX_Payments_TenantId_PaidUtc] ON [platform].[Payments] ([TenantId], [PaidUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN

    ALTER TABLE [platform].[CheckoutSessions] ADD CONSTRAINT [FK_CheckoutSessions_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
    ALTER TABLE [platform].[Payments] ADD CONSTRAINT [FK_Payments_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN

    UPDATE [platform].[Subscriptions] SET [StatusChangedUtc] = [CreatedUtc] WHERE [StatusChangedUtc] IS NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN

    INSERT INTO [platform].[Subscriptions] ([Id], [TenantId], [PlanId], [PlanPriceId], [Mode], [Status], [TrialEndsUtc], [CreatedUtc], [StatusChangedUtc], [CancelAtPeriodEnd], [OutstandingBalance])
    SELECT NEWID(), t.[Id], NULL, NULL, N'Free', N'Active', NULL, SYSUTCDATETIME(), SYSUTCDATETIME(), 0, 0
    FROM [dbo].[Tenants] t
    WHERE NOT EXISTS (SELECT 1 FROM [platform].[Subscriptions] s WHERE s.[TenantId] = t.[Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015074938_M1f_PayPalBilling'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261015074938_M1f_PayPalBilling', N'8.0.32');
END;
GO

COMMIT;
GO

