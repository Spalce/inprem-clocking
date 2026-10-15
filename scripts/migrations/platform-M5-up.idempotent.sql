BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015170549_M5_LegacyBillingMoved'
)
BEGIN

    INSERT INTO [platform].[Payments] ([Id], [TenantId], [SubscriptionId], [Source], [ExternalId], [Amount], [Currency], [Status], [PaidUtc], [RefundedUtc], [RefundedAmount], [RecordedBy])
    SELECT NEWID(), i.[TenantId], ps.[Id], N'Offline', CONCAT(N'legacy-invoice:', i.[InvoiceNumber]), i.[Amount], UPPER(LEFT(i.[Currency], 3)), N'Completed',
           COALESCE(i.[PaidDate], i.[IssuedDate]), NULL, 0, N'migration:M5'
    FROM [dbo].[Invoices] i
    JOIN [dbo].[Tenants] t ON t.[Id] = i.[TenantId]
    LEFT JOIN [platform].[Subscriptions] ps ON ps.[TenantId] = i.[TenantId]
    WHERE i.[Status] = N'Paid' AND i.[Amount] > 0
      AND NOT EXISTS (SELECT 1 FROM [platform].[Payments] p WHERE p.[Source] = N'Offline' AND p.[ExternalId] = CONCAT(N'legacy-invoice:', i.[InvoiceNumber]));
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015170549_M5_LegacyBillingMoved'
)
BEGIN

    INSERT INTO [platform].[TenantNotes] ([Id], [TenantId], [Text], [Author], [CreatedUtc])
    SELECT NEWID(), ls.[TenantId],
           CONCAT(N'Moved from the old billing (migration M5): ', FORMAT(ls.[Amount], N'0.00'), N' ', ls.[Currency], N' ', LOWER(ls.[BillingCycle]),
                  N', status ', ls.[Status], N', period ', CONVERT(char(10), ls.[CurrentPeriodStart], 23), N' to ', CONVERT(char(10), ls.[CurrentPeriodEnd], 23),
                  N'. Paid old invoices are under payments; others stay in the old tables until invoices arrive (Phase 4).'),
           N'migration:M5', SYSUTCDATETIME()
    FROM [dbo].[Subscriptions] ls
    JOIN [dbo].[Tenants] t ON t.[Id] = ls.[TenantId]
    WHERE ls.[Amount] > 0
      AND NOT EXISTS (SELECT 1 FROM [platform].[TenantNotes] n WHERE n.[TenantId] = ls.[TenantId] AND n.[Author] = N'migration:M5');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015170549_M5_LegacyBillingMoved'
)
BEGIN

    UPDATE ps SET ps.[Mode] = N'Invoiced', ps.[CurrentPeriodStartUtc] = ls.[CurrentPeriodStart], ps.[CurrentPeriodEndUtc] = ls.[CurrentPeriodEnd]
    FROM [platform].[Subscriptions] ps
    JOIN [dbo].[Subscriptions] ls ON ls.[TenantId] = ps.[TenantId]
    WHERE ls.[Amount] > 0 AND ls.[Status] IN (N'Active', N'PastDue')
      AND ps.[Mode] = N'Free' AND ps.[PlanId] IS NULL AND ps.[ExternalSubscriptionId] IS NULL AND ps.[Status] = N'Active';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [platform].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015170549_M5_LegacyBillingMoved'
)
BEGIN
    INSERT INTO [platform].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261015170549_M5_LegacyBillingMoved', N'8.0.32');
END;
GO

COMMIT;
GO

