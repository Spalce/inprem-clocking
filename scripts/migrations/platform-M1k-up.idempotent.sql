BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    CREATE SEQUENCE [platform].[InvoiceNumbers] START WITH 100001 INCREMENT BY 1 NO MINVALUE NO MAXVALUE NO CYCLE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    ALTER TABLE [platform].[Payments] ADD [InvoiceId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    ALTER TABLE [platform].[Payments] ADD [Method] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    CREATE TABLE [platform].[Invoices] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] int NOT NULL,
        [SubscriptionId] uniqueidentifier NULL,
        [Source] nvarchar(20) NOT NULL,
        [Number] varchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Currency] char(3) NOT NULL,
        [Total] decimal(12,2) NOT NULL,
        [PaidAmount] decimal(12,2) NOT NULL,
        [RefundedAmount] decimal(12,2) NOT NULL,
        [IssueDate] date NOT NULL,
        [DueDate] date NOT NULL,
        [PeriodStart] date NULL,
        [PeriodEnd] date NULL,
        [Notes] nvarchar(1000) NULL,
        [ExternalInvoiceId] varchar(50) NULL,
        [PaymentUrl] nvarchar(2000) NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [CreatedBy] nvarchar(300) NOT NULL,
        [SentUtc] datetime2 NULL,
        [PaidUtc] datetime2 NULL,
        [CancelledUtc] datetime2 NULL,
        [CancelReason] nvarchar(500) NULL,
        CONSTRAINT [PK_Invoices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Invoices_Subscriptions_SubscriptionId] FOREIGN KEY ([SubscriptionId]) REFERENCES [platform].[Subscriptions] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    CREATE TABLE [platform].[InvoiceLines] (
        [Position] int NOT NULL,
        [InvoiceId] uniqueidentifier NOT NULL,
        [Description] nvarchar(200) NOT NULL,
        [Quantity] decimal(8,2) NOT NULL,
        [UnitAmount] decimal(12,2) NOT NULL,
        CONSTRAINT [PK_InvoiceLines] PRIMARY KEY ([InvoiceId], [Position]),
        CONSTRAINT [FK_InvoiceLines_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [platform].[Invoices] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    CREATE INDEX [IX_Payments_InvoiceId] ON [platform].[Payments] ([InvoiceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Invoices_ExternalInvoiceId] ON [platform].[Invoices] ([ExternalInvoiceId]) WHERE [ExternalInvoiceId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Invoices_Number] ON [platform].[Invoices] ([Number]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Invoices_OnePerPeriod] ON [platform].[Invoices] ([SubscriptionId], [PeriodStart]) WHERE [SubscriptionId] IS NOT NULL AND [PeriodStart] IS NOT NULL AND [Status] <> ''Cancelled''');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    CREATE INDEX [IX_Invoices_Status_DueDate] ON [platform].[Invoices] ([Status], [DueDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    CREATE INDEX [IX_Invoices_TenantId_IssueDate] ON [platform].[Invoices] ([TenantId], [IssueDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    ALTER TABLE [platform].[Payments] ADD CONSTRAINT [FK_Payments_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [platform].[Invoices] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN

    ALTER TABLE [platform].[Invoices] ADD CONSTRAINT [FK_Invoices_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016022134_M1k_Invoices'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016022134_M1k_Invoices', N'8.0.32');
END;
GO

COMMIT;
GO

