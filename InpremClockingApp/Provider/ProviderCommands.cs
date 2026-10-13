using Inprem.Platform.Application.Provider;

namespace InpremClockingApp.Provider;

/// <summary>
/// Server commands (design 5.2-5.4): <c>dotnet InpremClockingApp.dll provider &lt;command&gt;</c>. They
/// run after the app is built and before it starts, then exit; the web server is never started.
/// They print only results: never a code, a token or a link.
/// </summary>
public static class ProviderCommands
{
    public const string Verb = "provider";

    public const string Usage = """
        Usage:
          provider bootstrap --email <owner email> [--code <bootstrap code>] [--dry-run]
          provider recover --user <owner email> --approver <approver email> [--code <break-glass code>] [--dry-run]
          provider recover --replace --old <owner email> --new-email <new owner email>
                           --approver <approver 1> --approver <approver 2> --approval-ref <text>
                           [--code <break-glass code>] [--dry-run]
          provider recover --complete --id <recovery id> [--dry-run]

        Without --code, the code is read from the console (it then stays out of the shell history).
        """;

    /// <summary>True when the process was started to run a provider command instead of the web server.</summary>
    public static bool IsCommand(string[] args) => args.Length > 0 && string.Equals(args[0], Verb, StringComparison.OrdinalIgnoreCase);

    /// <summary>The arguments the web host may see: none of a provider command's (they are not configuration).</summary>
    public static string[] HostArgs(string[] args) => IsCommand(args) ? Array.Empty<string>() : args;

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter? output = null, TextReader? input = null)
    {
        output ??= Console.Out;
        input ??= Console.In;

        if (!TryParse(args, out var command, out var options, out var error))
        {
            await output.WriteLineAsync(error);
            await output.WriteLineAsync(Usage);
            return 2;
        }

        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ProviderSecurityService>();
        var actor = $"operator:{Environment.UserName}@{Environment.MachineName}";
        var dryRun = options.ContainsKey("dry-run");

        ProviderResult result;
        switch (command)
        {
            case "bootstrap":
                if (!Require(options, output, "email")) return 2;
                result = await provider.BootstrapAsync(new BootstrapRequest(One(options, "email"), await CodeAsync(options, output, input, "bootstrap"), actor, dryRun));
                break;

            case "recover" when options.ContainsKey("complete"):
                if (!Require(options, output, "id")) return 2;
                if (!Guid.TryParse(One(options, "id"), out var id))
                {
                    await output.WriteLineAsync("--id must be the recovery id printed when the recovery was started.");
                    return 2;
                }
                result = await provider.CompleteRecoveryAsync(id, actor, dryRun);
                break;

            case "recover" when options.ContainsKey("replace"):
                if (!Require(options, output, "old", "new-email", "approver", "approval-ref")) return 2;
                result = await provider.StartBreakGlassAsync(new BreakGlassRequest(One(options, "old"), await CodeAsync(options, output, input, "break-glass"),
                    options["approver"], actor, dryRun, Replace: true, NewEmail: One(options, "new-email"), ApprovalReference: One(options, "approval-ref")));
                break;

            case "recover":
                if (!Require(options, output, "user", "approver")) return 2;
                result = await provider.StartBreakGlassAsync(new BreakGlassRequest(One(options, "user"), await CodeAsync(options, output, input, "break-glass"),
                    options["approver"], actor, dryRun));
                break;

            default:
                await output.WriteLineAsync(Usage);
                return 2;
        }

        await output.WriteLineAsync((result.Succeeded ? "OK: " : "REFUSED: ") + result.Message);
        foreach (var line in result.Plan) await output.WriteLineAsync("  - " + line);
        return result.Succeeded ? 0 : 1;
    }

    private static bool TryParse(string[] args, out string command, out Dictionary<string, List<string>> options, out string error)
    {
        command = args.Length > 1 ? args[1].ToLowerInvariant() : "";
        options = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        error = "";
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dry-run", "replace", "complete" };

        for (var i = 2; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unexpected argument '{args[i]}'.";
                return false;
            }

            var name = args[i][2..];
            if (flags.Contains(name))
            {
                options[name] = new List<string>();
                continue;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"--{name} needs a value.";
                return false;
            }

            if (!options.TryGetValue(name, out var values)) options[name] = values = new List<string>();
            values.Add(args[++i]);
        }

        if (command is not ("bootstrap" or "recover"))
        {
            error = command.Length == 0 ? "No command given." : $"Unknown command '{command}'.";
            return false;
        }
        return true;
    }

    private static bool Require(Dictionary<string, List<string>> options, TextWriter output, params string[] names)
    {
        var missing = names.Where(n => !options.TryGetValue(n, out var v) || v.Count == 0).ToList();
        if (missing.Count == 0) return true;
        output.WriteLine($"Missing: {string.Join(", ", missing.Select(m => "--" + m))}.");
        output.WriteLine(Usage);
        return false;
    }

    private static string One(Dictionary<string, List<string>> options, string name) => options[name][0];

    private static async Task<string> CodeAsync(Dictionary<string, List<string>> options, TextWriter output, TextReader input, string kind)
    {
        if (options.TryGetValue("code", out var values) && values.Count > 0) return values[0];
        await output.WriteAsync($"Enter the {kind} code: ");
        return (await input.ReadLineAsync())?.Trim() ?? "";
    }
}
