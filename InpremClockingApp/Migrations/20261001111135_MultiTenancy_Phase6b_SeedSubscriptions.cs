using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InpremClockingApp.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenancy_Phase6b_SeedSubscriptions : Migration
    {
        // Data-only step (see multi-tenancy.md Part 2, Phase 6): every tenant that exists today
        // (Inprem plus any test tenants from Part 1's verification) gets a placeholder Active
        // subscription at $0/month, so no tenant is left in a null/undefined billing state once
        // Phase 9 wires Tenant.IsActive/Subscription.Status into a real access gate. The $0
        // amount is a deliberate placeholder, not a real price - multi-tenancy.md flags "decide
        // Inprem's actual subscription amount, or exempt it as the house account" as something
        // for the provider to decide before Phase 9 ships; this migration only guarantees the
        // row exists so that decision can be made via the Phase 7 provider portal instead of
        // another migration.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO [Subscriptions] ([TenantId], [Amount], [Currency], [BillingCycle], [Status], [CurrentPeriodStart], [CurrentPeriodEnd], [CreatedAt], [PaymentGateway], [ExternalSubscriptionId])
                SELECT t.[Id], 0, N'USD', N'Monthly', N'Active', SYSUTCDATETIME(), DATEADD(YEAR, 1, SYSUTCDATETIME()), SYSUTCDATETIME(), NULL, NULL
                FROM [Tenants] t
                WHERE NOT EXISTS (SELECT 1 FROM [Subscriptions] s WHERE s.[TenantId] = t.[Id]);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort: only removes rows that still match this migration's exact
            // placeholder shape, so it won't clobber real subscription data entered later
            // through the Phase 7 provider portal.
            migrationBuilder.Sql(@"
                DELETE FROM [Subscriptions]
                WHERE [Amount] = 0
                    AND [Currency] = N'USD'
                    AND [BillingCycle] = N'Monthly'
                    AND [Status] = N'Active'
                    AND [PaymentGateway] IS NULL
                    AND [ExternalSubscriptionId] IS NULL;
            ");
        }
    }
}
