using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inprem.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1i_Signups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Signups",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PlanPriceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VerifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TermsAcceptedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SmsOptIn = table.Column<bool>(type: "bit", nullable: false),
                    ResendWindowStartUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResendCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Signups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Signups_PlanPrices_PlanPriceId",
                        column: x => x.PlanPriceId,
                        principalSchema: "platform",
                        principalTable: "PlanPrices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Signups_PlanPriceId",
                schema: "platform",
                table: "Signups",
                column: "PlanPriceId");

            migrationBuilder.CreateIndex(
                name: "IX_Signups_TenantId",
                schema: "platform",
                table: "Signups",
                column: "TenantId",
                unique: true);

            // Foreign key to the approved app's dbo.Tenants (owned by ApplicationDbContext).
            migrationBuilder.Sql(@"
ALTER TABLE [platform].[Signups] ADD CONSTRAINT [FK_Signups_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Signups",
                schema: "platform");
        }
    }
}
