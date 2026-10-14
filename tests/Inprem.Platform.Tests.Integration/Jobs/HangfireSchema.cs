using Microsoft.Data.SqlClient;

namespace Inprem.Platform.Tests.Integration.Jobs;

/// <summary>Installs the Hangfire tables from scripts/migrations/hangfire-up.idempotent.sql, as a deployment does.</summary>
public static class HangfireSchema
{
    public static async Task InstallAsync(string connectionString)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InpremClockingApp.sln"))) dir = dir.Parent;
        var script = await File.ReadAllTextAsync(Path.Combine(dir!.FullName, "scripts", "migrations", "hangfire-up.idempotent.sql"));
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(script, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }
}
