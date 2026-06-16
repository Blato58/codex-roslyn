using System.Security.Cryptography;
using System.Text;
using Codex.Roslyn.Abstractions.Dtos;
using Microsoft.CodeAnalysis;

namespace Codex.Roslyn.Core;

public sealed class SemanticSymbolIdService
{
    public string CreateSymbolId(string repoRoot, string solutionId, ISymbol symbol)
    {
        var docId = DocumentationCommentId.CreateDeclarationId(symbol);
        if (!string.IsNullOrWhiteSpace(docId))
        {
            var assembly = symbol.ContainingAssembly?.Name ?? "unknown";
            return $"csid:v1:{Hash(repoRoot)[..12]}:{solutionId}:{assembly}:{symbol.Kind.ToString().ToLowerInvariant()}:{Hash(docId)[..20]}";
        }

        var location = symbol.Locations.FirstOrDefault(location => location.IsInSource);
        if (location is not null)
        {
            var span = location.GetLineSpan();
            var path = Path.GetRelativePath(repoRoot, span.Path).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            var key = $"{path}:{span.StartLinePosition.Line + 1}:{span.StartLinePosition.Character + 1}:{span.EndLinePosition.Line + 1}:{span.EndLinePosition.Character + 1}:{symbol.Kind}:{symbol.Name}";
            return $"csid:v1:{Hash(repoRoot)[..12]}:{solutionId}:local:{Hash(key)[..20]}";
        }

        return $"csid:v1:{Hash(repoRoot)[..12]}:{solutionId}:symbol:{Hash(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))[..20]}";
    }

    public SemanticLocation? CreateLocation(string repoRoot, Location? location)
    {
        if (location is null || !location.IsInSource)
        {
            return null;
        }

        var span = location.GetLineSpan();
        return new SemanticLocation
        {
            File = Path.GetRelativePath(repoRoot, span.Path).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/'),
            StartLine = span.StartLinePosition.Line + 1,
            StartColumn = span.StartLinePosition.Character + 1,
            EndLine = span.EndLinePosition.Line + 1,
            EndColumn = span.EndLinePosition.Character + 1
        };
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
