namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record DocumentOutlineItem
{
    public required string DeclarationId { get; init; }

    public required string Kind { get; init; }

    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public int StartLine { get; init; }

    public int StartColumn { get; init; }

    public int EndLine { get; init; }

    public int EndColumn { get; init; }

    public string Confidence { get; init; } = "syntax_only";
}
