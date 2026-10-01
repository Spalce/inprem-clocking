using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InpremClockingApp.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoicePeriodUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_SubscriptionId",
                table: "Invoices");

            migrationBuilder.CreateIndex(
                name: "ActiveInvoicePerPeriodIndex",
                table: "Invoices",
                columns: new[] { "SubscriptionId", "PeriodStart" },
                unique: true,
                filter: "[Status] <> 'Void'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ActiveInvoicePerPeriodIndex",
                table: "Invoices");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SubscriptionId",
                table: "Invoices",
                column: "SubscriptionId");
        }
    }
}
