using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace InpremClockingApp.Tests.Baseline.Inspection;

/// <summary>
/// Runs a scripts/inspection/*.sql file the way sqlcmd would: sqlcmd commands (lines starting
/// with ':') are dropped, $(ConfirmLocalCopy) is substituted, and batches are split on GO.
/// Returns every result set of every batch, in order.
/// </summary>
public static class InspectionScript
{
    public sealed class ResultSet : List<Dictionary<string, object?>>
    {
        public object? Value(int row, string column) => this[row][column];
    }

    public static string PathOf(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InpremClockingApp.sln")))
        {
            dir = dir.Parent;
        }
        if (dir == null) throw new InvalidOperationException("Repository root (InpremClockingApp.sln) not found.");
        return Path.Combine(dir.FullName, "scripts", "inspection", fileName);
    }

    public static async Task<List<ResultSet>> RunAsync(string connectionString, string fileName, string confirmLocalCopy = "YES")
    {
        var text = await File.ReadAllTextAsync(PathOf(fileName));
        text = text.Replace("$(ConfirmLocalCopy)", confirmLocalCopy);
        var lines = text.Split('\n').Where(l => !l.TrimStart().StartsWith(':'));
        var batches = Regex.Split(string.Join('\n', lines), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Where(b => !string.IsNullOrWhiteSpace(b));

        var results = new List<ResultSet>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var batch in batches)
        {
            await using var command = new SqlCommand(batch, connection);
            await using var reader = await command.ExecuteReaderAsync();
            do
            {
                if (reader.FieldCount == 0) continue;
                var set = new ResultSet();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    set.Add(row);
                }
                results.Add(set);
            } while (await reader.NextResultAsync());
        }
        return results;
    }

    /// <summary>Result 1 of 04-data-quality.sql as check text -> problem count.</summary>
    public static Dictionary<string, int> Summary(ResultSet summary) =>
        summary.ToDictionary(r => (string)r["Check_"]!, r => Convert.ToInt32(r["Problems"]));
}
