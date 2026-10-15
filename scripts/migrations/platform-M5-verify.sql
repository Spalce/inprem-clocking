-- Verification for platform migration M5_LegacyBillingMoved. READ-ONLY. Counts only.
-- After the up script: Applied = 1, MovedPayments = LegacyPaidInvoices, Notes = LegacyPaying, MovedToInvoiced <= LegacyPaying.
-- LeftForReview: old subscriptions with a price not moved (not Active/PastDue, or the platform row was already changed): check them in the console.
-- OtherInvoices: unpaid or void old invoices, left in the old table for Phase 4.
SET NOCOUNT ON;
IF OBJECT_ID(N'[platform].[__EFMigrationsHistory]') IS NULL OR OBJECT_ID(N'[dbo].[Invoices]') IS NULL
BEGIN
    SELECT 0 AS Applied, NULL AS LegacyPaying, NULL AS MovedToInvoiced, NULL AS Notes, NULL AS LegacyPaidInvoices, NULL AS MovedPayments, NULL AS LeftForReview, NULL AS OtherInvoices;
    RETURN;
END
EXEC sp_executesql N'
SELECT
    (SELECT COUNT(*) FROM [platform].[__EFMigrationsHistory] WHERE MigrationId = N''20261015170549_M5_LegacyBillingMoved'') AS Applied,
    (SELECT COUNT(*) FROM [dbo].[Subscriptions] WHERE Amount > 0) AS LegacyPaying,
    (SELECT COUNT(*) FROM [platform].[Subscriptions] ps WHERE ps.Mode = N''Invoiced'' AND EXISTS (SELECT 1 FROM [platform].[TenantNotes] n WHERE n.TenantId = ps.TenantId AND n.Author = N''migration:M5'')) AS MovedToInvoiced,
    (SELECT COUNT(*) FROM [platform].[TenantNotes] WHERE Author = N''migration:M5'') AS Notes,
    (SELECT COUNT(*) FROM [dbo].[Invoices] WHERE Status = N''Paid'' AND Amount > 0) AS LegacyPaidInvoices,
    (SELECT COUNT(*) FROM [platform].[Payments] WHERE RecordedBy = N''migration:M5'') AS MovedPayments,
    (SELECT COUNT(*) FROM [dbo].[Subscriptions] ls WHERE ls.Amount > 0 AND NOT EXISTS (SELECT 1 FROM [platform].[Subscriptions] ps WHERE ps.TenantId = ls.TenantId AND ps.Mode = N''Invoiced'')) AS LeftForReview,
    (SELECT COUNT(*) FROM [dbo].[Invoices] WHERE NOT (Status = N''Paid'' AND Amount > 0)) AS OtherInvoices;';
GO
