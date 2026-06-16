using Codex.Roslyn.Index;

namespace Codex.Roslyn.Tests;

public sealed class SyntaxDeclarationIndexerTests
{
    [Fact]
    public void Index_ExtractsCommonCSharpDeclarations()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "Sample.cs");
        File.WriteAllText(
            file,
            """
            namespace Demo;

            public interface IService { void Run(); }
            public record Customer(int Id);
            public enum Mode { A }
            public delegate void Changed();

            public class CustomerService
            {
                private readonly string field;
                public event EventHandler? ChangedEvent;
                public CustomerService() { field = ""; }
                public string Name { get; set; } = "";
                public void GetAsync(int id) { }

                public class Nested { }
            }
            """);
        var scanned = new ScannedFile(file, "Sample.cs", ".cs", false, new FileHasher().Hash(file));

        var declarations = new SyntaxDeclarationIndexer().Index("repo_test", scanned);

        Assert.Contains(declarations, declaration => declaration.Kind == "interface" && declaration.Name == "IService");
        Assert.Contains(declarations, declaration => declaration.Kind == "record" && declaration.Name == "Customer");
        Assert.Contains(declarations, declaration => declaration.Kind == "enum" && declaration.Name == "Mode");
        Assert.Contains(declarations, declaration => declaration.Kind == "delegate" && declaration.Name == "Changed");
        Assert.Contains(declarations, declaration => declaration.Kind == "class" && declaration.Name == "CustomerService");
        Assert.Contains(declarations, declaration => declaration.Kind == "field" && declaration.Name == "field");
        Assert.Contains(declarations, declaration => declaration.Kind == "event" && declaration.Name == "ChangedEvent");
        Assert.Contains(declarations, declaration => declaration.Kind == "constructor" && declaration.Name == "CustomerService");
        Assert.Contains(declarations, declaration => declaration.Kind == "property" && declaration.Name == "Name");
        Assert.Contains(declarations, declaration => declaration.Kind == "method" && declaration.Name == "GetAsync");
        Assert.Contains(declarations, declaration => declaration.Kind == "class" && declaration.Name == "Nested" && declaration.ContainingType == "CustomerService");
    }

    [Fact]
    public void Index_ProducesStableSymbolIds()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "Sample.cs");
        File.WriteAllText(file, "namespace Demo; public class CustomerService { public void GetAsync() { } }");
        var scanned = new ScannedFile(file, "Sample.cs", ".cs", false, new FileHasher().Hash(file));
        var indexer = new SyntaxDeclarationIndexer();

        var first = indexer.Index("repo_test", scanned).Select(declaration => declaration.SymbolId).ToArray();
        var second = indexer.Index("repo_test", scanned).Select(declaration => declaration.SymbolId).ToArray();

        Assert.Equal(first, second);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexRoslynTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
