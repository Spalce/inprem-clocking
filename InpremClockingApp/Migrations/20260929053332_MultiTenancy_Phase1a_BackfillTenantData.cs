using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InpremClockingApp.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenancy_Phase1a_BackfillTenantData : Migration
    {
        // Data-only step (see multi-tenancy.md, "Migration strategy for existing data",
        // steps 1-2): create the one tenant that owns all of today's existing data, and
        // backfill every row's TenantId to it, while the columns are still nullable. The
        // schema is tightened to NOT NULL + tenant-scoped unique indexes in the next
        // migration (Phase1b), once every row already has a valid TenantId.
        private const string TenantName = "Inprem Holistic Community Resource Center";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                INSERT INTO [Tenants] ([Name], [TimeZoneId], [IsActive], [CreatedAt])
                VALUES (N'{TenantName}', N'America/New_York', 1, SYSUTCDATETIME());

                DECLARE @TenantId INT = SCOPE_IDENTITY();

                UPDATE [Staffs] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
                UPDATE [Volunteers] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
                UPDATE [ClockingsStaff] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
                UPDATE [Clockings] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
                UPDATE [Setting] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
                UPDATE [AspNetUsers] SET [TenantId] = @TenantId WHERE [TenantId] IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                DECLARE @TenantId INT = (SELECT TOP 1 [Id] FROM [Tenants] WHERE [Name] = N'{TenantName}');

                UPDATE [Staffs] SET [TenantId] = NULL WHERE [TenantId] = @TenantId;
                UPDATE [Volunteers] SET [TenantId] = NULL WHERE [TenantId] = @TenantId;
                UPDATE [ClockingsStaff] SET [TenantId] = NULL WHERE [TenantId] = @TenantId;
                UPDATE [Clockings] SET [TenantId] = NULL WHERE [TenantId] = @TenantId;
                UPDATE [Setting] SET [TenantId] = NULL WHERE [TenantId] = @TenantId;
                UPDATE [AspNetUsers] SET [TenantId] = NULL WHERE [TenantId] = @TenantId;

                DELETE FROM [Tenants] WHERE [Id] = @TenantId;
            ");
        }
    }
}
