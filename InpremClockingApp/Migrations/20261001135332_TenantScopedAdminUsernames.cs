using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InpremClockingApp.Migrations
{
    /// <inheritdoc />
    public partial class TenantScopedAdminUsernames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "UserNameIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "SuperAdminNormalizedUserNameIndex",
                table: "AspNetUsers",
                columns: new[] { "TenantId", "NormalizedUserName" },
                unique: true,
                filter: "[TenantId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "TenantNormalizedUserNameIndex",
                table: "AspNetUsers",
                columns: new[] { "TenantId", "NormalizedUserName" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "SuperAdminNormalizedUserNameIndex",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "TenantNormalizedUserNameIndex",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "UserNameIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");
        }
    }
}
