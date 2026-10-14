using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inprem.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1d_Organizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSignInUtc",
                schema: "platform",
                table: "TenantMemberships",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlanPriceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TrialEndsUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_PlanPrices_PlanPriceId",
                        column: x => x.PlanPriceId,
                        principalSchema: "platform",
                        principalTable: "PlanPrices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Plans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "platform",
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantNotes",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Author = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantProfiles",
                schema: "platform",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Slug = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StatusReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StatusChangedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantProfiles", x => x.TenantId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PlanId",
                schema: "platform",
                table: "Subscriptions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PlanPriceId",
                schema: "platform",
                table: "Subscriptions",
                column: "PlanPriceId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_TenantId",
                schema: "platform",
                table: "Subscriptions",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantNotes_TenantId_CreatedUtc",
                schema: "platform",
                table: "TenantNotes",
                columns: new[] { "TenantId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantProfiles_Slug",
                schema: "platform",
                table: "TenantProfiles",
                column: "Slug",
                unique: true);

            // Foreign keys to the approved app's dbo.Tenants (owned by ApplicationDbContext).
            migrationBuilder.Sql(@"
ALTER TABLE [platform].[TenantProfiles] ADD CONSTRAINT [FK_TenantProfiles_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
ALTER TABLE [platform].[TenantNotes] ADD CONSTRAINT [FK_TenantNotes_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
ALTER TABLE [platform].[Subscriptions] ADD CONSTRAINT [FK_Subscriptions_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);");

            // Every existing organization gets a profile: slug org-{Id} (unique; editable in the
            // console), status from Tenants.IsActive. Re-runnable: existing profiles are left alone.
            migrationBuilder.Sql(@"
INSERT INTO [platform].[TenantProfiles] ([TenantId], [Slug], [Status], [StatusReason], [StatusChangedUtc], [CreatedUtc])
SELECT t.[Id], CONCAT('org-', t.[Id]), CASE WHEN t.[IsActive] = 1 THEN N'Active' ELSE N'Suspended' END,
       CASE WHEN t.[IsActive] = 1 THEN NULL ELSE N'Inactive before the Provider Console (migration M1d)' END,
       NULL, t.[CreatedAt]
FROM [dbo].[Tenants] t
WHERE NOT EXISTS (SELECT 1 FROM [platform].[TenantProfiles] p WHERE p.[TenantId] = t.[Id]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Subscriptions",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "TenantNotes",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "TenantProfiles",
                schema: "platform");

            migrationBuilder.DropColumn(
                name: "LastSignInUtc",
                schema: "platform",
                table: "TenantMemberships");
        }
    }
}
