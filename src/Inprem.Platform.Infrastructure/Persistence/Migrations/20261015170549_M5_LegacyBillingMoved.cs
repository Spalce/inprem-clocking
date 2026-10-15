using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inprem.Platform.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Migration M5 (Phase 2 step 2.9): the approved app's old billing rows (dbo.Subscriptions,
    /// dbo.Invoices, written by the removed C4 pages) are carried into the platform tables. Data
    /// only, no schema change; re-runnable; the old tables stay until M6 (Phase 7).
    /// <list type="bullet">
    /// <item>Paid old invoices become offline payments (<c>legacy-invoice:{number}</c>, once).</item>
    /// <item>An old subscription with a price, Active or PastDue, whose platform subscription is
    /// still the M1f Free backfill, becomes Invoiced (still Active: nothing is blocked), with its
    /// old period.</item>
    /// <item>Every old subscription with a price gets one organization note with its old terms.</item>
    /// <item>Everything else (unpaid or void invoices, old subscriptions in other states) is left for
    /// review: <c>platform-M5-verify.sql</c> counts it; invoices come in Phase 4.</item>
    /// </list>
    /// </summary>
    public partial class M5_LegacyBillingMoved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO [platform].[Payments] ([Id], [TenantId], [SubscriptionId], [Source], [ExternalId], [Amount], [Currency], [Status], [PaidUtc], [RefundedUtc], [RefundedAmount], [RecordedBy])
SELECT NEWID(), i.[TenantId], ps.[Id], N'Offline', CONCAT(N'legacy-invoice:', i.[InvoiceNumber]), i.[Amount], UPPER(LEFT(i.[Currency], 3)), N'Completed',
       COALESCE(i.[PaidDate], i.[IssuedDate]), NULL, 0, N'migration:M5'
FROM [dbo].[Invoices] i
JOIN [dbo].[Tenants] t ON t.[Id] = i.[TenantId]
LEFT JOIN [platform].[Subscriptions] ps ON ps.[TenantId] = i.[TenantId]
WHERE i.[Status] = N'Paid' AND i.[Amount] > 0
  AND NOT EXISTS (SELECT 1 FROM [platform].[Payments] p WHERE p.[Source] = N'Offline' AND p.[ExternalId] = CONCAT(N'legacy-invoice:', i.[InvoiceNumber]));");

            migrationBuilder.Sql(@"
INSERT INTO [platform].[TenantNotes] ([Id], [TenantId], [Text], [Author], [CreatedUtc])
SELECT NEWID(), ls.[TenantId],
       CONCAT(N'Moved from the old billing (migration M5): ', FORMAT(ls.[Amount], N'0.00'), N' ', ls.[Currency], N' ', LOWER(ls.[BillingCycle]),
              N', status ', ls.[Status], N', period ', CONVERT(char(10), ls.[CurrentPeriodStart], 23), N' to ', CONVERT(char(10), ls.[CurrentPeriodEnd], 23),
              N'. Paid old invoices are under payments; others stay in the old tables until invoices arrive (Phase 4).'),
       N'migration:M5', SYSUTCDATETIME()
FROM [dbo].[Subscriptions] ls
JOIN [dbo].[Tenants] t ON t.[Id] = ls.[TenantId]
WHERE ls.[Amount] > 0
  AND NOT EXISTS (SELECT 1 FROM [platform].[TenantNotes] n WHERE n.[TenantId] = ls.[TenantId] AND n.[Author] = N'migration:M5');");

            migrationBuilder.Sql(@"
UPDATE ps SET ps.[Mode] = N'Invoiced', ps.[CurrentPeriodStartUtc] = ls.[CurrentPeriodStart], ps.[CurrentPeriodEndUtc] = ls.[CurrentPeriodEnd]
FROM [platform].[Subscriptions] ps
JOIN [dbo].[Subscriptions] ls ON ls.[TenantId] = ps.[TenantId]
WHERE ls.[Amount] > 0 AND ls.[Status] IN (N'Active', N'PastDue')
  AND ps.[Mode] = N'Free' AND ps.[PlanId] IS NULL AND ps.[ExternalSubscriptionId] IS NULL AND ps.[Status] = N'Active';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to the M1f state: the moved subscriptions return to Free (only those M5 moved: they
            // carry its note and are still Invoiced without a plan), then the notes and payments go.
            migrationBuilder.Sql(@"
UPDATE ps SET ps.[Mode] = N'Free', ps.[CurrentPeriodStartUtc] = NULL, ps.[CurrentPeriodEndUtc] = NULL
FROM [platform].[Subscriptions] ps
WHERE ps.[Mode] = N'Invoiced' AND ps.[PlanId] IS NULL AND ps.[ExternalSubscriptionId] IS NULL
  AND EXISTS (SELECT 1 FROM [platform].[TenantNotes] n WHERE n.[TenantId] = ps.[TenantId] AND n.[Author] = N'migration:M5');
DELETE FROM [platform].[TenantNotes] WHERE [Author] = N'migration:M5';
DELETE FROM [platform].[Payments] WHERE [RecordedBy] = N'migration:M5';");
        }
    }
}
