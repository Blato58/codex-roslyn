namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record FlowAnalysisResult
{
    public string File { get; init; } = string.Empty;

    public int Line { get; init; }

    public string Kind { get; init; } = string.Empty;

    public IReadOnlyList<string> Reads { get; init; } = [];

    public IReadOnlyList<string> Writes { get; init; } = [];

    public IReadOnlyList<string> Captured { get; init; } = [];

    public IReadOnlyList<string> Operations { get; init; } = [];
}
