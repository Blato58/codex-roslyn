namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record PublicApiDiffResult
{
    public string Project { get; init; } = string.Empty;

    public IReadOnlyList<string> PublicSymbols { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
