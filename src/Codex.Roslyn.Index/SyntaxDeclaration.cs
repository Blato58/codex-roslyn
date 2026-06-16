namespace Codex.Roslyn.Index;

public sealed record SyntaxDeclaration(
    string DeclarationId,
    string SymbolId,
    string Kind,
    string Name,
    string Namespace,
    string? ContainingType,
    string? Accessibility,
    string Modifiers,
    int Arity,
    string SignatureShort,
    string SignatureHash,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string SyntaxHash);
