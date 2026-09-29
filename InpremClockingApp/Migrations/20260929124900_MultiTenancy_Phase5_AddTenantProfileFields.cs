using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InpremClockingApp.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenancy_Phase5_AddTenantProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Tenants",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactInfo",
                table: "Tenants",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            // Backfills Inprem's own address/contact, which used to be hardcoded directly into
            // every report/PDF template - keeps its own output effectively unchanged now that
            // those templates read from the tenant row instead.
            migrationBuilder.Sql(@"
                UPDATE Tenants
                SET Address = N'5757 Karl Road, Columbus, OH 43229',
                    ContactInfo = N'614-516-1812 | Inpremcommunitycenter@yahoo.com'
                WHERE Name = N'Inprem Holistic Community Resource Center';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ContactInfo",
                table: "Tenants");
        }
    }
}
