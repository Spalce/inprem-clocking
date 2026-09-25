using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InpremClockingApp.Migrations
{
    /// <inheritdoc />
    public partial class AddClockDateUniquePerDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClockingsStaff_StafId",
                table: "ClockingsStaff");

            migrationBuilder.DropIndex(
                name: "IX_Clockings_VoluntId",
                table: "Clockings");

            migrationBuilder.AddColumn<DateTime>(
                name: "ClockDate",
                table: "ClockingsStaff",
                type: "date",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "ClockDate",
                table: "Clockings",
                type: "date",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Backfill existing rows' ClockDate from their CreatedAt (stored UTC), converted to the
            // org's local calendar day - the same conversion OrgClock uses at runtime - so historical
            // rows land on the day they actually happened for the org, not the UTC day. This must run
            // before the unique indexes below: every row currently shares the same placeholder default,
            // which would collide across any staff/volunteer with more than one historical session.
            migrationBuilder.Sql(@"
                UPDATE ClockingsStaff
                SET ClockDate = CAST(CreatedAt AT TIME ZONE 'UTC' AT TIME ZONE 'Eastern Standard Time' AS DATE)
                WHERE CreatedAt IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE Clockings
                SET ClockDate = CAST(CreatedAt AT TIME ZONE 'UTC' AT TIME ZONE 'Eastern Standard Time' AS DATE)
                WHERE CreatedAt IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_ClockingsStaff_StafId_ClockDate",
                table: "ClockingsStaff",
                columns: new[] { "StafId", "ClockDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clockings_VoluntId_ClockDate",
                table: "Clockings",
                columns: new[] { "VoluntId", "ClockDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClockingsStaff_StafId_ClockDate",
                table: "ClockingsStaff");

            migrationBuilder.DropIndex(
                name: "IX_Clockings_VoluntId_ClockDate",
                table: "Clockings");

            migrationBuilder.DropColumn(
                name: "ClockDate",
                table: "ClockingsStaff");

            migrationBuilder.DropColumn(
                name: "ClockDate",
                table: "Clockings");

            migrationBuilder.CreateIndex(
                name: "IX_ClockingsStaff_StafId",
                table: "ClockingsStaff",
                column: "StafId");

            migrationBuilder.CreateIndex(
                name: "IX_Clockings_VoluntId",
                table: "Clockings",
                column: "VoluntId");
        }
    }
}
