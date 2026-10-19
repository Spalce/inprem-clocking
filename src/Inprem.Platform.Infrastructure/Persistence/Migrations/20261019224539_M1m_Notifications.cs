using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inprem.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1m_Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsentRecords",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Phone = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    PersonRef = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Source = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Wording = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RecordedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: true),
                    RecipientType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RecipientRef = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Destination = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TemplateKey = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Urgency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    EventKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SkipReason = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    ExternalId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(10,5)", precision: 10, scale: 5, nullable: true),
                    EstimatedCost = table.Column<decimal>(type: "decimal(10,5)", precision: 10, scale: 5, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NotBeforeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplates",
                schema: "platform",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplates", x => new { x.Key, x.Channel });
                });

            migrationBuilder.CreateTable(
                name: "TenantMessagingSettings",
                schema: "platform",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    SmsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MonthlySmsLimit = table.Column<int>(type: "int", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantMessagingSettings", x => x.TenantId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentRecords_Phone_RecordedUtc",
                schema: "platform",
                table: "ConsentRecords",
                columns: new[] { "Phone", "RecordedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentRecords_TenantId",
                schema: "platform",
                table: "ConsentRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Channel_CreatedUtc",
                schema: "platform",
                table: "Notifications",
                columns: new[] { "Channel", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_EventKey_RecipientRef_Channel",
                schema: "platform",
                table: "Notifications",
                columns: new[] { "EventKey", "RecipientRef", "Channel" },
                unique: true,
                filter: "[EventKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ExternalId",
                schema: "platform",
                table: "Notifications",
                column: "ExternalId",
                unique: true,
                filter: "[ExternalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Status_NotBeforeUtc",
                schema: "platform",
                table: "Notifications",
                columns: new[] { "Status", "NotBeforeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_Channel_CreatedUtc",
                schema: "platform",
                table: "Notifications",
                columns: new[] { "TenantId", "Channel", "CreatedUtc" });

            // Foreign keys to the approved app's dbo.Tenants (owned by ApplicationDbContext). Consent
            // records keep none: they are the proof of consent and are kept as long as the law asks.
            migrationBuilder.Sql(@"
ALTER TABLE [platform].[Notifications] ADD CONSTRAINT [FK_Notifications_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);
ALTER TABLE [platform].[TenantMessagingSettings] ADD CONSTRAINT [FK_TenantMessagingSettings_Tenants_TenantId]
    FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsentRecords",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "NotificationTemplates",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "TenantMessagingSettings",
                schema: "platform");
        }
    }
}
