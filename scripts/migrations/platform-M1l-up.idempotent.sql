BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016063325_M1l_PaymentRefunds'
)
BEGIN
    CREATE TABLE [platform].[PaymentRefunds] (
        [Id] uniqueidentifier NOT NULL,
        [PaymentId] uniqueidentifier NOT NULL,
        [TenantId] int NOT NULL,
        [ExternalRefundId] varchar(100) NULL,
        [Amount] decimal(10,2) NOT NULL,
        [Currency] char(3) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Reason] nvarchar(300) NOT NULL,
        [RefundedUtc] datetime2 NOT NULL,
        [RecordedBy] nvarchar(300) NOT NULL,
        CONSTRAINT [PK_PaymentRefunds] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PaymentRefunds_Payments_PaymentId] FOREIGN KEY ([PaymentId]) REFERENCES [platform].[Payments] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016063325_M1l_PaymentRefunds'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_PaymentRefunds_ExternalRefundId] ON [platform].[PaymentRefunds] ([ExternalRefundId]) WHERE [ExternalRefundId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016063325_M1l_PaymentRefunds'
)
BEGIN
    CREATE INDEX [IX_PaymentRefunds_PaymentId] ON [platform].[PaymentRefunds] ([PaymentId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016063325_M1l_PaymentRefunds'
)
BEGIN
    CREATE INDEX [IX_PaymentRefunds_TenantId_RefundedUtc] ON [platform].[PaymentRefunds] ([TenantId], [RefundedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016063325_M1l_PaymentRefunds'
)
BEGIN

    ALTER TABLE [platform].[PaymentRefunds] ADD CONSTRAINT [FK_PaymentRefunds_Tenants_TenantId]
        FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016063325_M1l_PaymentRefunds'
)
BEGIN
    EXEC(N'
    INSERT INTO [platform].[PaymentRefunds] ([Id], [PaymentId], [TenantId], [ExternalRefundId], [Amount], [Currency], [Status], [Reason], [RefundedUtc], [RecordedBy])
    SELECT NEWID(), p.[Id], p.[TenantId], NULL, p.[RefundedAmount], p.[Currency], N''Completed'', N''Refunded before refunds were recorded one by one (M1l)'',
           COALESCE(p.[RefundedUtc], p.[PaidUtc]), N''migration:M1l''
    FROM [platform].[Payments] p
    WHERE p.[Status] = N''Refunded'' AND p.[RefundedAmount] > 0
      AND NOT EXISTS (SELECT 1 FROM [platform].[PaymentRefunds] r WHERE r.[PaymentId] = p.[Id]);')
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016063325_M1l_PaymentRefunds'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016063325_M1l_PaymentRefunds', N'8.0.32');
END;
GO

COMMIT;
GO

