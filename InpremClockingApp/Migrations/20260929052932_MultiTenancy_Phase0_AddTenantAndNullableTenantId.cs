using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InpremClockingApp.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenancy_Phase0_AddTenantAndNullableTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Volunteers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Staffs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Setting",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ClockingsStaff",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Clockings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Volunteers_TenantId",
                table: "Volunteers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Staffs_TenantId",
                table: "Staffs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Setting_TenantId",
                table: "Setting",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClockingsStaff_TenantId",
                table: "ClockingsStaff",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Clockings_TenantId",
                table: "Clockings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Tenants_TenantId",
                table: "AspNetUsers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Clockings_Tenants_TenantId",
                table: "Clockings",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClockingsStaff_Tenants_TenantId",
                table: "ClockingsStaff",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Setting_Tenants_TenantId",
                table: "Setting",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Staffs_Tenants_TenantId",
                table: "Staffs",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Volunteers_Tenants_TenantId",
                table: "Volunteers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Tenants_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Clockings_Tenants_TenantId",
                table: "Clockings");

            migrationBuilder.DropForeignKey(
                name: "FK_ClockingsStaff_Tenants_TenantId",
                table: "ClockingsStaff");

            migrationBuilder.DropForeignKey(
                name: "FK_Setting_Tenants_TenantId",
                table: "Setting");

            migrationBuilder.DropForeignKey(
                name: "FK_Staffs_Tenants_TenantId",
                table: "Staffs");

            migrationBuilder.DropForeignKey(
                name: "FK_Volunteers_Tenants_TenantId",
                table: "Volunteers");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Volunteers_TenantId",
                table: "Volunteers");

            migrationBuilder.DropIndex(
                name: "IX_Staffs_TenantId",
                table: "Staffs");

            migrationBuilder.DropIndex(
                name: "IX_Setting_TenantId",
                table: "Setting");

            migrationBuilder.DropIndex(
                name: "IX_ClockingsStaff_TenantId",
                table: "ClockingsStaff");

            migrationBuilder.DropIndex(
                name: "IX_Clockings_TenantId",
                table: "Clockings");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Volunteers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Staffs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Setting");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ClockingsStaff");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Clockings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AspNetUsers");
        }
    }
}
