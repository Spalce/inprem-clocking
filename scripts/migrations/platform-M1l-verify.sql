-- Verification for platform migration M1l_PaymentRefunds. READ-ONLY. Counts only.
-- After the up script: Applied = 1, Table = 1, ForeignKeys = 2, Missing = 0 (refunded payments without a refund row).
-- Refunds: rows (one per refund made before M1l at first).
SET NOCOUNT ON;
IF OBJECT_ID(N'[platform].[PaymentRefunds]') IS NULL
BEGIN
    SELECT 0 AS Applied, 0 AS [Table], 0 AS ForeignKeys, NULL AS Missing, NULL AS Refunds;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261016063325_M1l_PaymentRefunds'') AS Applied,
    1 AS [Table],
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE SCHEMA_NAME(schema_id) = N''platform'' AND name IN (N''FK_PaymentRefunds_Payments_PaymentId'', N''FK_PaymentRefunds_Tenants_TenantId'')) AS ForeignKeys,
    (SELECT COUNT(*) FROM [platform].[Payments] p WHERE p.[Status] = N''Refunded'' AND p.[RefundedAmount] > 0
        AND NOT EXISTS (SELECT 1 FROM [platform].[PaymentRefunds] r WHERE r.[PaymentId] = p.[Id])) AS Missing,
    (SELECT COUNT(*) FROM [platform].[PaymentRefunds]) AS Refunds;';
GO
