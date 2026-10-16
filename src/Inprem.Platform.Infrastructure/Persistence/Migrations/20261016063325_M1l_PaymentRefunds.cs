using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inprem.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1l_PaymentRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentRefunds",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    ExternalRefundId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    RefundedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRefunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRefunds_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalSchema: "platform",
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_ExternalRefundId",
                schema: "platform",
                table: "PaymentRefunds",
                column: "ExternalRefundId",
                unique: true,
                filter: "[ExternalRefundId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_PaymentId",
                schema: "platform",
                table: "PaymentRefunds",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_TenantId_RefundedUtc",
                schema: "platform",
                table: "PaymentRefunds",
                columns: new[] { "TenantId", "RefundedUtc" });

            // Foreign key to the approved app's dbo.Tenants (owned by ApplicationDbContext).
            migrationBuilder.Sql(@"
ALTER TABLE [platform].[PaymentRefunds] ADD CONSTRAINT [FK_PaymentRefunds_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);");

            // Payments refunded before this table (Phase 2 webhooks kept only the total): one row each,
            // so totals keep adding up from the rows. Re-runnable: only payments without a row.
            migrationBuilder.Sql(@"EXEC(N'
INSERT INTO [platform].[PaymentRefunds] ([Id], [PaymentId], [TenantId], [ExternalRefundId], [Amount], [Currency], [Status], [Reason], [RefundedUtc], [RecordedBy])
SELECT NEWID(), p.[Id], p.[TenantId], NULL, p.[RefundedAmount], p.[Currency], N''Completed'', N''Refunded before refunds were recorded one by one (M1l)'',
       COALESCE(p.[RefundedUtc], p.[PaidUtc]), N''migration:M1l''
FROM [platform].[Payments] p
WHERE p.[Status] = N''Refunded'' AND p.[RefundedAmount] > 0
  AND NOT EXISTS (SELECT 1 FROM [platform].[PaymentRefunds] r WHERE r.[PaymentId] = p.[Id]);')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentRefunds",
                schema: "platform");
        }
    }
}
