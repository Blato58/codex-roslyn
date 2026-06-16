namespace Codex.Roslyn.Index;

public sealed record RepoScanResult(
    IReadOnlyList<string> SolutionPaths,
    IReadOnlyList<ScannedFile> SourceFiles,
    IReadOnlyList<string> ProjectAndConfigPaths);

public sealed record ScannedFile(
    string FullPath,
    string RelativePath,
    string Extension,
    bool IsGenerated,
    FileHashInfo HashInfo);

public sealed record FileHashInfo(
    long SizeBytes,
    DateTimeOffset MTimeUtc,
    string ContentHash);
