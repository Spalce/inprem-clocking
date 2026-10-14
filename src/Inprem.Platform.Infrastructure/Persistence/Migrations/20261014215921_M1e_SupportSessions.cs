using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inprem.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1e_SupportSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportSessions",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    ProviderUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProviderEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndedBy = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    HandoffTokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    HandoffExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HandoffUsedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportSessions_HandoffTokenHash",
                schema: "platform",
                table: "SupportSessions",
                column: "HandoffTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportSessions_TenantId_StartedUtc",
                schema: "platform",
                table: "SupportSessions",
                columns: new[] { "TenantId", "StartedUtc" });

            migrationBuilder.Sql(@"
ALTER TABLE [platform].[SupportSessions] ADD CONSTRAINT [FK_SupportSessions_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportSessions",
                schema: "platform");
        }
    }
}
