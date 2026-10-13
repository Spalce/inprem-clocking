using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inprem.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1b_ProviderSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderInvitations",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderInvitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderUsers",
                schema: "platform",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderUsers", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryRecords",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TargetEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NewEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Operator = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    FirstApproverEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FirstApprovalTokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    FirstApprovedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirstWaivedDelay = table.Column<bool>(type: "bit", nullable: false),
                    SecondApproverEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SecondApprovalTokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    SecondApprovedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SecondWaivedDelay = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovalExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NotBeforeUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityAttempts",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsedCodes",
                schema: "platform",
                columns: table => new
                {
                    CodeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UsedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsedCodes", x => x.CodeHash);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredUtc",
                schema: "platform",
                table: "AuditEntries",
                column: "OccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderInvitations_TokenHash",
                schema: "platform",
                table: "ProviderInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderInvitations_UserId",
                schema: "platform",
                table: "ProviderInvitations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryRecords_FirstApprovalTokenHash",
                schema: "platform",
                table: "RecoveryRecords",
                column: "FirstApprovalTokenHash",
                unique: true,
                filter: "[FirstApprovalTokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryRecords_SecondApprovalTokenHash",
                schema: "platform",
                table: "RecoveryRecords",
                column: "SecondApprovalTokenHash",
                unique: true,
                filter: "[SecondApprovalTokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryRecords_TargetUserId_CreatedUtc",
                schema: "platform",
                table: "RecoveryRecords",
                columns: new[] { "TargetUserId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAttempts_Kind_OccurredUtc",
                schema: "platform",
                table: "SecurityAttempts",
                columns: new[] { "Kind", "OccurredUtc" });

            // Foreign keys to the approved app's AspNetUsers (owned by ApplicationDbContext). An
            // account that is a provider user, or has a provider invitation, cannot be deleted by
            // accident. Recovery records and audit entries keep plain ids: they are history.
            migrationBuilder.Sql(@"
ALTER TABLE [platform].[ProviderUsers] ADD CONSTRAINT [FK_ProviderUsers_AspNetUsers_UserId]
    FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]);
ALTER TABLE [platform].[ProviderInvitations] ADD CONSTRAINT [FK_ProviderInvitations_AspNetUsers_UserId]
    FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]);");

            // The audit log is append-only in the database too: any UPDATE or DELETE fails.
            // (EXEC: CREATE TRIGGER must start its own batch, and idempotent scripts wrap each statement.)
            migrationBuilder.Sql(@"
EXEC(N'CREATE TRIGGER [platform].[TR_AuditEntries_AppendOnly] ON [platform].[AuditEntries]
INSTEAD OF UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 50010, ''platform.AuditEntries is append-only: entries cannot be changed or deleted.'', 1;
END');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "ProviderInvitations",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "ProviderUsers",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "RecoveryRecords",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "SecurityAttempts",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "UsedCodes",
                schema: "platform");
        }
    }
}
