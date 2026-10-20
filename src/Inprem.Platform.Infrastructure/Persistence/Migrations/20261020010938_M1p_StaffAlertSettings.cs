using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inprem.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M1p_StaffAlertSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AdminSummaryEnabled",
                schema: "platform",
                table: "TenantMessagingSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LongBreakMinutes",
                schema: "platform",
                table: "TenantMessagingSettings",
                type: "int",
                nullable: false,
                defaultValue: 60); // D32: rows that exist get the default long break

            migrationBuilder.AddColumn<TimeOnly>(
                name: "MissedClockOutTime",
                schema: "platform",
                table: "TenantMessagingSettings",
                type: "time",
                nullable: false,
                defaultValue: new TimeOnly(21, 0, 0)); // D32: rows that exist get the default missed clock-out time

            migrationBuilder.AddColumn<bool>(
                name: "StaffAlertsEnabled",
                schema: "platform",
                table: "TenantMessagingSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminSummaryEnabled",
                schema: "platform",
                table: "TenantMessagingSettings");

            migrationBuilder.DropColumn(
                name: "LongBreakMinutes",
                schema: "platform",
                table: "TenantMessagingSettings");

            migrationBuilder.DropColumn(
                name: "MissedClockOutTime",
                schema: "platform",
                table: "TenantMessagingSettings");

            migrationBuilder.DropColumn(
                name: "StaffAlertsEnabled",
                schema: "platform",
                table: "TenantMessagingSettings");
        }
    }
}
