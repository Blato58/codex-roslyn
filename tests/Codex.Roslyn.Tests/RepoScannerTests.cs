using Codex.Roslyn.Index;

namespace Codex.Roslyn.Tests;

public sealed class RepoScannerTests
{
    [Fact]
    public void Scan_SkipsExcludedDirectoriesAndGeneratedFiles()
    {
        var root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "App.slnx"), string.Empty);
        File.WriteAllText(Path.Combine(root, "Keep.cs"), "class Keep { }");
        File.WriteAllText(Path.Combine(root, "Skip.generated.cs"), "class Skip { }");
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        File.WriteAllText(Path.Combine(root, "bin", "Ignored.cs"), "class Ignored { }");

        var scanner = new RepoScanner(new FileHasher());

        var result = scanner.Scan(root);

        Assert.Equal(["App.slnx"], result.SolutionPaths);
        var source = Assert.Single(result.SourceFiles);
        Assert.Equal("Keep.cs", source.RelativePath);
        Assert.False(source.IsGenerated);
    }

    [Fact]
    public void Hash_ChangesWhenFileContentChanges()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "File.cs");
        File.WriteAllText(file, "class A { }");
        var hasher = new FileHasher();

        var first = hasher.Hash(file);
        File.WriteAllText(file, "class B { }");
        var second = hasher.Hash(file);

        Assert.NotEqual(first.ContentHash, second.ContentHash);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexRoslynTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
