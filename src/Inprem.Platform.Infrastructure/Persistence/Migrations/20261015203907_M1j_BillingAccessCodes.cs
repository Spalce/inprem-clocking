using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inprem.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1j_BillingAccessCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingAccessCodes",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    CodeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingAccessCodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingAccessCodes_CodeHash",
                schema: "platform",
                table: "BillingAccessCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingAccessCodes_TenantId_CreatedUtc",
                schema: "platform",
                table: "BillingAccessCodes",
                columns: new[] { "TenantId", "CreatedUtc" });

            // Foreign key to the approved app's dbo.Tenants (owned by ApplicationDbContext).
            migrationBuilder.Sql(@"
ALTER TABLE [platform].[BillingAccessCodes] ADD CONSTRAINT [FK_BillingAccessCodes_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingAccessCodes",
                schema: "platform");
        }
    }
}
