namespace Codex.Roslyn.Core;

public sealed record BashGuardResult(string? AdditionalContext)
{
    public bool HasWarning => !string.IsNullOrWhiteSpace(AdditionalContext);
}
