namespace Codex.Roslyn.Mcp;

public sealed record McpHttpServerOptions(int Port, string Path, string? BearerToken)
{
    public const int DefaultPort = 38777;
    public const string DefaultPath = "/mcp";
    public const string TokenEnvironmentVariable = "CODEX_ROSLYN_MCP_TOKEN";

    public static McpHttpServerOptions FromArgs(string[] args)
    {
        var port = DefaultPort;
        var portValue = GetOptionValue(args, "--port");
        if (!string.IsNullOrWhiteSpace(portValue)
            && (!int.TryParse(portValue, out port) || port < 1 || port > 65535))
        {
            throw new ArgumentException($"Invalid --port value '{portValue}'.");
        }

        var path = GetOptionValue(args, "--path") ?? DefaultPath;
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        var token = GetOptionValue(args, "--token")
            ?? Environment.GetEnvironmentVariable(TokenEnvironmentVariable);

        return new McpHttpServerOptions(port, path, string.IsNullOrWhiteSpace(token) ? null : token);
    }

    private static string? GetOptionValue(string[] args, string optionName)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < args.Length ? args[i + 1] : null;
            }
        }

        return null;
    }
}
