using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inprem.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1f_PayPalBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CancelAtPeriodEnd",
                schema: "platform",
                table: "Subscriptions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CurrentPeriodEndUtc",
                schema: "platform",
                table: "Subscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CurrentPeriodStartUtc",
                schema: "platform",
                table: "Subscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSubscriptionId",
                schema: "platform",
                table: "Subscriptions",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FreeAccessUntilUtc",
                schema: "platform",
                table: "Subscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxPeopleOverride",
                schema: "platform",
                table: "Subscriptions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OutstandingBalance",
                schema: "platform",
                table: "Subscriptions",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "StatusChangedUtc",
                schema: "platform",
                table: "Subscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalProductId",
                schema: "platform",
                table: "Plans",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillingEvents",
                schema: "platform",
                columns: table => new
                {
                    ExternalEventId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    EventType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    ResourceType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    ResourceId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceivedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingEvents", x => x.ExternalEventId);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutSessions",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    SignupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Purpose = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PlanPriceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayPalPlanId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExternalSubscriptionId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    ApprovalUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReturnUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CancelUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutSessions_PlanPrices_PlanPriceId",
                        column: x => x.PlanPriceId,
                        principalSchema: "platform",
                        principalTable: "PlanPrices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PaidUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RefundedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundedAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalSchema: "platform",
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_ExternalSubscriptionId",
                schema: "platform",
                table: "Subscriptions",
                column: "ExternalSubscriptionId",
                unique: true,
                filter: "[ExternalSubscriptionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEvents_ReceivedUtc",
                schema: "platform",
                table: "BillingEvents",
                column: "ReceivedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEvents_ResourceId",
                schema: "platform",
                table: "BillingEvents",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_ExternalSubscriptionId",
                schema: "platform",
                table: "CheckoutSessions",
                column: "ExternalSubscriptionId",
                unique: true,
                filter: "[ExternalSubscriptionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_IdempotencyKey",
                schema: "platform",
                table: "CheckoutSessions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_PlanPriceId",
                schema: "platform",
                table: "CheckoutSessions",
                column: "PlanPriceId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_Reference",
                schema: "platform",
                table: "CheckoutSessions",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_Status_ExpiresUtc",
                schema: "platform",
                table: "CheckoutSessions",
                columns: new[] { "Status", "ExpiresUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_TenantId",
                schema: "platform",
                table: "CheckoutSessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Source_ExternalId",
                schema: "platform",
                table: "Payments",
                columns: new[] { "Source", "ExternalId" },
                unique: true,
                filter: "[ExternalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_SubscriptionId",
                schema: "platform",
                table: "Payments",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_PaidUtc",
                schema: "platform",
                table: "Payments",
                columns: new[] { "TenantId", "PaidUtc" });

            // Foreign keys to the approved app's dbo.Tenants (owned by ApplicationDbContext).
            migrationBuilder.Sql(@"
ALTER TABLE [platform].[CheckoutSessions] ADD CONSTRAINT [FK_CheckoutSessions_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
ALTER TABLE [platform].[Payments] ADD CONSTRAINT [FK_Payments_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);");

            // Existing subscriptions: the status counts from their creation.
            migrationBuilder.Sql(@"
UPDATE [platform].[Subscriptions] SET [StatusChangedUtc] = [CreatedUtc] WHERE [StatusChangedUtc] IS NULL;");

            // D26: every organization without a subscription (created before the console, including
            // Inprem's own house organization) gets a Free one, so nothing changes for it when
            // billing is enforced (step 2.7). The provider can move it to a paid plan in the console.
            // Re-runnable: organizations with a subscription are left alone.
            migrationBuilder.Sql(@"
INSERT INTO [platform].[Subscriptions] ([Id], [TenantId], [PlanId], [PlanPriceId], [Mode], [Status], [TrialEndsUtc], [CreatedUtc], [StatusChangedUtc], [CancelAtPeriodEnd], [OutstandingBalance])
SELECT NEWID(), t.[Id], NULL, NULL, N'Free', N'Active', NULL, SYSUTCDATETIME(), SYSUTCDATETIME(), 0, 0
FROM [dbo].[Tenants] t
WHERE NOT EXISTS (SELECT 1 FROM [platform].[Subscriptions] s WHERE s.[TenantId] = t.[Id]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingEvents",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "CheckoutSessions",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "Payments",
                schema: "platform");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_ExternalSubscriptionId",
                schema: "platform",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "CancelAtPeriodEnd",
                schema: "platform",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "CurrentPeriodEndUtc",
                schema: "platform",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "CurrentPeriodStartUtc",
                schema: "platform",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ExternalSubscriptionId",
                schema: "platform",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "FreeAccessUntilUtc",
                schema: "platform",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "MaxPeopleOverride",
                schema: "platform",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "OutstandingBalance",
                schema: "platform",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "StatusChangedUtc",
                schema: "platform",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "PayPalProductId",
                schema: "platform",
                table: "Plans");
        }
    }
}
