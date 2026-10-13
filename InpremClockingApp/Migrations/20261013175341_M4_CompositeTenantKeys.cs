using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InpremClockingApp.Migrations
{
    /// <inheritdoc />
    public partial class M4_CompositeTenantKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guard (Phase 0 step 0.5): stop before changing anything if a session is filed under
            // another organization than its person's - the composite keys could not be created, and
            // each case needs a decision. scripts/inspection/04-data-quality.sql, result 4, lists them.
            migrationBuilder.Sql(@"
DECLARE @crossed int =
    (SELECT COUNT(*) FROM [Clockings] c JOIN [Volunteers] v ON v.VolunteerId = c.VoluntId WHERE c.TenantId <> v.TenantId)
  + (SELECT COUNT(*) FROM [ClockingsStaff] cs JOIN [Staffs] s ON s.StaffId = cs.StafId WHERE cs.TenantId <> s.TenantId);
IF @crossed > 0
    THROW 50004, N'M4_CompositeTenantKeys stopped: some sessions belong to another organization than their person. Resolve them first (see scripts/inspection/04-data-quality.sql, result 4).', 1;");

            migrationBuilder.DropForeignKey(
                name: "FK_Clockings_Volunteers_VoluntId",
                table: "Clockings");

            migrationBuilder.DropForeignKey(
                name: "FK_ClockingsStaff_Staffs_StafId",
                table: "ClockingsStaff");

            migrationBuilder.DropIndex(
                name: "IX_ClockingsStaff_StafId",
                table: "ClockingsStaff");

            migrationBuilder.DropIndex(
                name: "IX_Clockings_VoluntId",
                table: "Clockings");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Volunteers_TenantId_VolunteerId",
                table: "Volunteers",
                columns: new[] { "TenantId", "VolunteerId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Staffs_TenantId_StaffId",
                table: "Staffs",
                columns: new[] { "TenantId", "StaffId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Clockings_Volunteers_TenantId_VoluntId",
                table: "Clockings",
                columns: new[] { "TenantId", "VoluntId" },
                principalTable: "Volunteers",
                principalColumns: new[] { "TenantId", "VolunteerId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ClockingsStaff_Staffs_TenantId_StafId",
                table: "ClockingsStaff",
                columns: new[] { "TenantId", "StafId" },
                principalTable: "Staffs",
                principalColumns: new[] { "TenantId", "StaffId" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Clockings_Volunteers_TenantId_VoluntId",
                table: "Clockings");

            migrationBuilder.DropForeignKey(
                name: "FK_ClockingsStaff_Staffs_TenantId_StafId",
                table: "ClockingsStaff");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Volunteers_TenantId_VolunteerId",
                table: "Volunteers");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Staffs_TenantId_StaffId",
                table: "Staffs");

            migrationBuilder.CreateIndex(
                name: "IX_ClockingsStaff_StafId",
                table: "ClockingsStaff",
                column: "StafId");

            migrationBuilder.CreateIndex(
                name: "IX_Clockings_VoluntId",
                table: "Clockings",
                column: "VoluntId");

            migrationBuilder.AddForeignKey(
                name: "FK_Clockings_Volunteers_VoluntId",
                table: "Clockings",
                column: "VoluntId",
                principalTable: "Volunteers",
                principalColumn: "VolunteerId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ClockingsStaff_Staffs_StafId",
                table: "ClockingsStaff",
                column: "StafId",
                principalTable: "Staffs",
                principalColumn: "StaffId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
