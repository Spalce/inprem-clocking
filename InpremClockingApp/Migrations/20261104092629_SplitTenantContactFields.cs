using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InpremClockingApp.Migrations
{
    /// <inheritdoc />
    public partial class SplitTenantContactFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Tenants",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Tenants",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE Tenants
                SET Phone = CASE WHEN CHARINDEX(N' | ', ContactInfo) > 0 THEN LEFT(ContactInfo, CHARINDEX(N' | ', ContactInfo) - 1) ELSE ContactInfo END,
                    Email = CASE WHEN CHARINDEX(N' | ', ContactInfo) > 0 THEN SUBSTRING(ContactInfo, CHARINDEX(N' | ', ContactInfo) + 3, 255) END;
            ");

            migrationBuilder.DropColumn(
                name: "ContactInfo",
                table: "Tenants");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "Volunteers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "Volunteers",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "Volunteers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Approved");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactInfo",
                table: "Tenants",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE Tenants
                SET ContactInfo = NULLIF(LTRIM(RTRIM(CONCAT(ISNULL(Phone, N''),
                    CASE WHEN Phone IS NOT NULL AND Email IS NOT NULL THEN N' | ' ELSE N'' END,
                    ISNULL(Email, N'')))), N'');
            ");

            migrationBuilder.DropColumn(name: "Email", table: "Tenants");
            migrationBuilder.DropColumn(name: "Phone", table: "Tenants");
            migrationBuilder.DropColumn(name: "ApprovedAt", table: "Volunteers");
            migrationBuilder.DropColumn(name: "ApprovedBy", table: "Volunteers");
            migrationBuilder.DropColumn(name: "ApprovalStatus", table: "Volunteers");
        }
    }
}
