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
                "C# semantic tooling is available. Avoid broad C# source dumps; start with cs_repo_overview, cs_symbol_search, cs_symbol_at, or cs_find_references and request compact results first.");
        }

        if (IsBroadSymbolSearch(command))
        {
            return new BashGuardResult(
                "For C# symbol lookup, prefer cs_symbol_search before broad shell search. Use rg only after semantic lookup is insufficient or exact source context is needed.");
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
            || RgAllCsRegex().IsMatch(normalized);
    }

    private static bool IsBroadSymbolSearch(string command)
    {
        var normalized = command.Trim();
        if (!normalized.StartsWith("rg ", StringComparison.OrdinalIgnoreCase))
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
            && !normalized.Contains("SPECIFICATION.md", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("RESEARCH.md", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"cat\s+\$\(\s*find\s+\.?\s+.*-name\s+['""]?\*\.cs['""]?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CatFindCsRegex();

    [GeneratedRegex(@"find\s+\.?\s+.*-name\s+['""]?\*\.cs['""]?.*-exec\s+cat\s+\{\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FindExecCatCsRegex();

    [GeneratedRegex(@"rg\s+\.?\s+.*(\*\.cs|'[^']*\.cs'|""[^""]*\.cs"")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RgAllCsRegex();
}
