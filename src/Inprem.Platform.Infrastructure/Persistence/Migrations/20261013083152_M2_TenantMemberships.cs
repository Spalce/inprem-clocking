using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inprem.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M2_TenantMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantMemberships",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InvitedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantMemberships", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantMemberships_TenantId",
                schema: "platform",
                table: "TenantMemberships",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantMemberships_UserId_TenantId",
                schema: "platform",
                table: "TenantMemberships",
                columns: new[] { "UserId", "TenantId" },
                unique: true);

            // Foreign keys to the approved app's tables (owned by ApplicationDbContext, so not in
            // this context's model). Deleting an account removes its memberships; an organization
            // with memberships cannot be deleted by accident.
            migrationBuilder.Sql(@"
ALTER TABLE [platform].[TenantMemberships] ADD CONSTRAINT [FK_TenantMemberships_AspNetUsers_UserId]
    FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE;
ALTER TABLE [platform].[TenantMemberships] ADD CONSTRAINT [FK_TenantMemberships_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);");

            // Backfill (design M2): one Active membership for every account that belongs to an
            // organization. An organization with exactly one admin gets that admin as Owner.
            // With several admins, all become Admin: AspNetUsers has no creation date, so "the
            // earliest admin" cannot be derived; the Owner is chosen later (verification query
            // lists those organizations). Accounts without an organization (platform operators)
            // get no membership. Re-runnable: existing memberships are left alone.
            migrationBuilder.Sql(@"
INSERT INTO [platform].[TenantMemberships] ([Id], [UserId], [TenantId], [Role], [Status], [InvitedByUserId], [CreatedUtc])
SELECT NEWID(), u.[Id], u.[TenantId],
       CASE WHEN COUNT(*) OVER (PARTITION BY u.[TenantId]) = 1 THEN N'Owner' ELSE N'Admin' END,
       N'Active', NULL, SYSUTCDATETIME()
FROM [dbo].[AspNetUsers] u
WHERE u.[TenantId] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM [platform].[TenantMemberships] m WHERE m.[UserId] = u.[Id] AND m.[TenantId] = u.[TenantId]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantMemberships",
                schema: "platform");
        }
    }
}
