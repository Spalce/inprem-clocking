using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InpremClockingApp.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenancy_Phase1b_TightenSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Volunteers_EmailAddress",
                table: "Volunteers");

            migrationBuilder.DropIndex(
                name: "IX_Volunteers_TenantId",
                table: "Volunteers");

            migrationBuilder.DropIndex(
                name: "IX_Staffs_EmailAddress",
                table: "Staffs");

            migrationBuilder.DropIndex(
                name: "IX_Staffs_TenantId",
                table: "Staffs");

            migrationBuilder.DropIndex(
                name: "IX_Setting_TenantId",
                table: "Setting");

            migrationBuilder.DropIndex(
                name: "IX_ClockingsStaff_StafId_ClockDate",
                table: "ClockingsStaff");

            migrationBuilder.DropIndex(
                name: "IX_ClockingsStaff_TenantId",
                table: "ClockingsStaff");

            migrationBuilder.DropIndex(
                name: "IX_Clockings_TenantId",
                table: "Clockings");

            migrationBuilder.DropIndex(
                name: "IX_Clockings_VoluntId_ClockDate",
                table: "Clockings");

            migrationBuilder.AlterColumn<int>(
                name: "TenantId",
                table: "Volunteers",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TenantId",
                table: "Staffs",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TenantId",
                table: "Setting",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TenantId",
                table: "ClockingsStaff",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TenantId",
                table: "Clockings",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Volunteers_TenantId_EmailAddress",
                table: "Volunteers",
                columns: new[] { "TenantId", "EmailAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Staffs_TenantId_EmailAddress",
                table: "Staffs",
                columns: new[] { "TenantId", "EmailAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Setting_TenantId",
                table: "Setting",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClockingsStaff_StafId",
                table: "ClockingsStaff",
                column: "StafId");

            migrationBuilder.CreateIndex(
                name: "IX_ClockingsStaff_TenantId_StafId_ClockDate",
                table: "ClockingsStaff",
                columns: new[] { "TenantId", "StafId", "ClockDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clockings_TenantId_VoluntId_ClockDate",
                table: "Clockings",
                columns: new[] { "TenantId", "VoluntId", "ClockDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clockings_VoluntId",
                table: "Clockings",
                column: "VoluntId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Volunteers_TenantId_EmailAddress",
                table: "Volunteers");

            migrationBuilder.DropIndex(
                name: "IX_Staffs_TenantId_EmailAddress",
                table: "Staffs");

            migrationBuilder.DropIndex(
                name: "IX_Setting_TenantId",
                table: "Setting");

            migrationBuilder.DropIndex(
                name: "IX_ClockingsStaff_StafId",
                table: "ClockingsStaff");

            migrationBuilder.DropIndex(
                name: "IX_ClockingsStaff_TenantId_StafId_ClockDate",
                table: "ClockingsStaff");

            migrationBuilder.DropIndex(
                name: "IX_Clockings_TenantId_VoluntId_ClockDate",
                table: "Clockings");

            migrationBuilder.DropIndex(
                name: "IX_Clockings_VoluntId",
                table: "Clockings");

            migrationBuilder.AlterColumn<int>(
                name: "TenantId",
                table: "Volunteers",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "TenantId",
                table: "Staffs",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "TenantId",
                table: "Setting",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "TenantId",
                table: "ClockingsStaff",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "TenantId",
                table: "Clockings",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateIndex(
                name: "IX_Volunteers_EmailAddress",
                table: "Volunteers",
                column: "EmailAddress",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Volunteers_TenantId",
                table: "Volunteers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Staffs_EmailAddress",
                table: "Staffs",
                column: "EmailAddress",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Staffs_TenantId",
                table: "Staffs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Setting_TenantId",
                table: "Setting",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClockingsStaff_StafId_ClockDate",
                table: "ClockingsStaff",
                columns: new[] { "StafId", "ClockDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClockingsStaff_TenantId",
                table: "ClockingsStaff",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Clockings_TenantId",
                table: "Clockings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Clockings_VoluntId_ClockDate",
                table: "Clockings",
                columns: new[] { "VoluntId", "ClockDate" },
                unique: true);
        }
    }
}
