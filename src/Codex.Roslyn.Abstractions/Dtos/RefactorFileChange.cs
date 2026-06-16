namespace Codex.Roslyn.Abstractions.Dtos;

public sealed record RefactorFileChange
{
    public string File { get; init; } = string.Empty;

    public int Edits { get; init; }
}
