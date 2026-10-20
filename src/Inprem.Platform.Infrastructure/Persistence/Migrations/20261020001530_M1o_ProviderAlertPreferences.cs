using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inprem.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1o_ProviderAlertPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderAlertPreferences",
                schema: "platform",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    NewSignups = table.Column<bool>(type: "bit", nullable: false),
                    BillingProblems = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SystemProblems = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderAlertPreferences", x => x.UserId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderAlertPreferences",
                schema: "platform");
        }
    }
}
