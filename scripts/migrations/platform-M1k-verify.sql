-- Verification for platform migration M1k_Invoices. READ-ONLY. Counts only.
-- Foreign keys are counted in the platform schema only: the old app's dbo.Invoices uses two of the same names.
-- After the up script: Applied = 1, Tables = 2, Sequence = 1, ForeignKeys = 4, PaymentColumns = 2. Invoices: rows (0 at first).
SET NOCOUNT ON;
IF OBJECT_ID(N'[platform].[Invoices]') IS NULL
BEGIN
    SELECT 0 AS Applied, 0 AS Tables, 0 AS [Sequence], 0 AS ForeignKeys, 0 AS PaymentColumns, NULL AS Invoices;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261016022134_M1k_Invoices'') AS Applied,
    (SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = N''platform'' AND t.name IN (N''Invoices'', N''InvoiceLines'')) AS Tables,
    (SELECT COUNT(*) FROM sys.sequences q JOIN sys.schemas s ON s.schema_id = q.schema_id WHERE s.name = N''platform'' AND q.name = N''InvoiceNumbers'') AS [Sequence],
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE SCHEMA_NAME(schema_id) = N''platform'' AND name IN (N''FK_Invoices_Tenants_TenantId'', N''FK_Invoices_Subscriptions_SubscriptionId'', N''FK_InvoiceLines_Invoices_InvoiceId'', N''FK_Payments_Invoices_InvoiceId'')) AS ForeignKeys,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N''[platform].[Payments]'') AND name IN (N''InvoiceId'', N''Method'')) AS PaymentColumns,
    (SELECT COUNT(*) FROM [platform].[Invoices]) AS Invoices;';
GO
