using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InpremClockingApp.Migrations
{
    /// <inheritdoc />
    public partial class M3_PlatformUniqueEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guard (Phase 0 step 0.4): stop before changing anything if two accounts share an
            // email. Each case needs a decision (rename a test account, or merge into one person with
            // two memberships); scripts/inspection/04-data-quality.sql lists them.
            migrationBuilder.Sql(@"
DECLARE @shared int = (SELECT COUNT(*) FROM (
    SELECT NormalizedEmail FROM [AspNetUsers] WHERE NormalizedEmail IS NOT NULL
    GROUP BY NormalizedEmail HAVING COUNT(*) > 1) d);
IF @shared > 0
    THROW 50003, N'M3_PlatformUniqueEmail stopped: some emails are used by more than one account. Resolve them first (see scripts/inspection/04-data-quality.sql, result 2).', 1;");

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true,
                filter: "[NormalizedEmail] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");
        }
    }
}
