using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InpremClockingApp.Migrations
{
    /// <inheritdoc />
    public partial class AddVolunteerCategoryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactPerson",
                table: "Volunteers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstitutionName",
                table: "Volunteers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MandateType",
                table: "Volunteers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaceOfWork",
                table: "Volunteers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VolunteerCategory",
                table: "Volunteers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactPerson",
                table: "Volunteers");

            migrationBuilder.DropColumn(
                name: "InstitutionName",
                table: "Volunteers");

            migrationBuilder.DropColumn(
                name: "MandateType",
                table: "Volunteers");

            migrationBuilder.DropColumn(
                name: "PlaceOfWork",
                table: "Volunteers");

            migrationBuilder.DropColumn(
                name: "VolunteerCategory",
                table: "Volunteers");
        }
    }
}
