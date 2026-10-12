using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace InpremClockingApp.Tests.Support.Database;

/// <summary>
/// Supplies a SQL Server instance for tests. Which one is chosen by the environment variable
/// <c>INPREM_TEST_DB</c>:
/// <list type="bullet">
/// <item><c>localdb</c> — Windows LocalDB, <c>(localdb)\MSSQLLocalDB</c> (default on Windows).</item>
/// <item><c>container</c> — a throwaway SQL Server container via Testcontainers (default elsewhere; needs Docker).</item>
/// <item><c>external</c> — an existing server given by <c>INPREM_TEST_SQL</c> (used by CI's service container).</item>
/// </list>
/// The server is shared by the whole test run; every fixture creates its own database on it.
/// Never point this at a shared or live server.
/// </summary>
public static class TestSqlServer
{
    public const string ModeVariable = "INPREM_TEST_DB";
    public const string ExternalConnectionVariable = "INPREM_TEST_SQL";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _serverConnectionString;
    private static MsSqlContainer? _container;

    public static string Mode
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(ModeVariable);
            if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim().ToLowerInvariant();
            return OperatingSystem.IsWindows() ? "localdb" : "container";
        }
    }

    /// <summary>Connection string for a new, not-yet-created database with a unique name.</summary>
    public static async Task<string> NewDatabaseConnectionStringAsync(string purpose)
    {
        var server = await GetServerConnectionStringAsync().ConfigureAwait(false);
        var builder = new SqlConnectionStringBuilder(server)
        {
            InitialCatalog = $"InpremTest_{purpose}_{Guid.NewGuid():N}",
            MultipleActiveResultSets = true,
            TrustServerCertificate = true,
        };
        return builder.ConnectionString;
    }

    private static async Task<string> GetServerConnectionStringAsync()
    {
        if (_serverConnectionString != null) return _serverConnectionString;

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_serverConnectionString != null) return _serverConnectionString;

            _serverConnectionString = Mode switch
            {
                "localdb" => @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true",
                "external" => Environment.GetEnvironmentVariable(ExternalConnectionVariable)
                    ?? throw new InvalidOperationException(
                        $"{ModeVariable}=external requires {ExternalConnectionVariable} to hold a SQL Server connection string."),
                "container" => await StartContainerAsync().ConfigureAwait(false),
                var other => throw new InvalidOperationException(
                    $"Unknown {ModeVariable} value '{other}'. Use localdb, container or external."),
            };
            return _serverConnectionString;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<string> StartContainerAsync()
    {
        // Testcontainers' resource reaper removes the container when the test process exits.
        _container = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync().ConfigureAwait(false);
        return _container.GetConnectionString();
    }
}
