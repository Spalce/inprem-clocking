using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inprem.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1g_CheckoutPlanChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CheckoutSessions_ExternalSubscriptionId",
                schema: "platform",
                table: "CheckoutSessions");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_ExternalSubscriptionId",
                schema: "platform",
                table: "CheckoutSessions",
                column: "ExternalSubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CheckoutSessions_ExternalSubscriptionId",
                schema: "platform",
                table: "CheckoutSessions");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutSessions_ExternalSubscriptionId",
                schema: "platform",
                table: "CheckoutSessions",
                column: "ExternalSubscriptionId",
                unique: true,
                filter: "[ExternalSubscriptionId] IS NOT NULL");
        }
    }
}
