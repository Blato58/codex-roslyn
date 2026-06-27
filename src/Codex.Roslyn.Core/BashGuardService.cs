using System.Text.Json;
using System.Text.RegularExpressions;

namespace Codex.Roslyn.Core;

public sealed partial class BashGuardService
{
    public BashGuardResult EvaluateHookInput(string? hookInput)
    {
        var command = ExtractCommand(hookInput);
        return EvaluateCommand(command);
    }

    public BashGuardResult EvaluateCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new BashGuardResult(null);
        }

        if (IsBroadSourceDump(command))
        {
            return new BashGuardResult(
                "C# semantic tooling is available. Avoid broad C# source dumps; start with cs_repo_overview, use cs_index_build if the cold index is missing or stale, then use cs_symbol_search, cs_symbol_at, or cs_find_references with compact results first.");
        }

        if (IsBroadSymbolSearch(command))
        {
            return new BashGuardResult(
                "For C# symbol lookup, find usages, or find references, prefer cs_symbol_search, cs_symbol_at, or cs_find_references before broad shell search. Start with cs_repo_overview and use cs_index_build when the index is missing or stale. Use shell search only after semantic lookup is insufficient or exact source context is needed.");
        }

        if (IsBroadDotnetTest(command))
        {
            return new BashGuardResult(
                "For targeted .NET validation, prefer cs_test_impact first and run the returned dotnet test command. Broad dotnet test is still allowed when full-suite validation is intentional.");
        }

        return new BashGuardResult(null);
    }

    private static string? ExtractCommand(string? hookInput)
    {
        if (string.IsNullOrWhiteSpace(hookInput))
        {
            return null;
        }

        var trimmed = hookInput.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return trimmed;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return FindCommand(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindCommand(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("command") && property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value.GetString();
                    }

                    var nested = FindCommand(property.Value);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindCommand(item);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }

                break;
        }

        return null;
    }

    private static bool IsBroadSourceDump(string command)
    {
        var normalized = command.Trim();
        return CatFindCsRegex().IsMatch(normalized)
            || FindExecCatCsRegex().IsMatch(normalized)
            || GetChildItemGetContentCsRegex().IsMatch(normalized)
            || RgAllCsRegex().IsMatch(normalized);
    }

    private static bool IsBroadSymbolSearch(string command)
    {
        var normalized = command.Trim();
        if (!StartsWithBroadSearchTool(normalized))
        {
            return false;
        }

        if (RgAllCsRegex().IsMatch(normalized))
        {
            return false;
        }

        return !normalized.Contains("--files", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("--glob", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("-g", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("docs/SPECIFICATION.md", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("docs/RESEARCH.md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithBroadSearchTool(string command)
    {
        return command.StartsWith("rg ", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("grep ", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("Select-String ", StringComparison.OrdinalIgnoreCase)
            || GetChildItemSelectStringRegex().IsMatch(command);
    }

    private static bool IsBroadDotnetTest(string command)
    {
        var normalized = command.Trim();
        if (!normalized.StartsWith("dotnet test", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !normalized.Contains(".csproj", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains(".sln", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains(".slnx", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("--filter", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"cat\s+\$\(\s*find\s+\.?\s+.*-name\s+['""]?\*\.cs['""]?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CatFindCsRegex();

    [GeneratedRegex(@"find\s+\.?\s+.*-name\s+['""]?\*\.cs['""]?.*-exec\s+cat\s+\{\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FindExecCatCsRegex();

    [GeneratedRegex(@"Get-ChildItem\b.*(\*\.cs|Filter\s+['""]?\*\.cs|Include\s+['""]?\*\.cs).*?\|\s*Get-Content\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GetChildItemGetContentCsRegex();

    [GeneratedRegex(@"Get-ChildItem\b.*\|\s*Select-String\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GetChildItemSelectStringRegex();

    [GeneratedRegex(@"rg\s+\.?\s+.*(\*\.cs|'[^']*\.cs'|""[^""]*\.cs"")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RgAllCsRegex();
}
