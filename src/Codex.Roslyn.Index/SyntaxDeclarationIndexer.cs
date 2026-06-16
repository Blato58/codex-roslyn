using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Codex.Roslyn.Index;

public sealed class SyntaxDeclarationIndexer
{
    public IReadOnlyList<SyntaxDeclaration> Index(string repoId, ScannedFile file)
    {
        var source = File.ReadAllText(file.FullPath);
        var tree = CSharpSyntaxTree.ParseText(source, path: file.RelativePath);
        var root = tree.GetCompilationUnitRoot();
        var declarations = new List<SyntaxDeclaration>();

        VisitMembers(
            root.Members,
            tree,
            repoId,
            file,
            currentNamespace: string.Empty,
            containingTypes: [],
            declarations);

        return declarations;
    }

    private static void VisitMembers(
        SyntaxList<MemberDeclarationSyntax> members,
        SyntaxTree tree,
        string repoId,
        ScannedFile file,
        string currentNamespace,
        IReadOnlyList<string> containingTypes,
        List<SyntaxDeclaration> declarations)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case FileScopedNamespaceDeclarationSyntax fileScopedNamespace:
                    var fileScopedName = CombineNamespace(currentNamespace, fileScopedNamespace.Name.ToString());
                    VisitMembers(fileScopedNamespace.Members, tree, repoId, file, fileScopedName, containingTypes, declarations);
                    break;
                case BaseNamespaceDeclarationSyntax namespaceDeclaration:
                    var nestedNamespace = CombineNamespace(currentNamespace, namespaceDeclaration.Name.ToString());
                    VisitMembers(namespaceDeclaration.Members, tree, repoId, file, nestedNamespace, containingTypes, declarations);
                    break;
                case TypeDeclarationSyntax typeDeclaration:
                    AddTypeDeclaration(tree, repoId, file, currentNamespace, containingTypes, declarations, typeDeclaration);
                    var nestedTypes = containingTypes.Append(typeDeclaration.Identifier.ValueText).ToArray();
                    VisitMembers(typeDeclaration.Members, tree, repoId, file, currentNamespace, nestedTypes, declarations);
                    break;
                case EnumDeclarationSyntax enumDeclaration:
                    AddEnumDeclaration(tree, repoId, file, currentNamespace, containingTypes, declarations, enumDeclaration);
                    break;
                case DelegateDeclarationSyntax delegateDeclaration:
                    AddDelegateDeclaration(tree, repoId, file, currentNamespace, containingTypes, declarations, delegateDeclaration);
                    break;
                case MethodDeclarationSyntax methodDeclaration:
                    AddMemberDeclaration(tree, repoId, file, currentNamespace, containingTypes, declarations, methodDeclaration, "method", methodDeclaration.Identifier.ValueText, methodDeclaration.ParameterList.ToString());
                    break;
                case ConstructorDeclarationSyntax constructorDeclaration:
                    AddMemberDeclaration(tree, repoId, file, currentNamespace, containingTypes, declarations, constructorDeclaration, "constructor", constructorDeclaration.Identifier.ValueText, constructorDeclaration.ParameterList.ToString());
                    break;
                case PropertyDeclarationSyntax propertyDeclaration:
                    AddMemberDeclaration(tree, repoId, file, currentNamespace, containingTypes, declarations, propertyDeclaration, "property", propertyDeclaration.Identifier.ValueText, string.Empty);
                    break;
                case FieldDeclarationSyntax fieldDeclaration:
                    foreach (var variable in fieldDeclaration.Declaration.Variables)
                    {
                        AddVariableDeclaration(tree, repoId, file, currentNamespace, containingTypes, declarations, fieldDeclaration, "field", variable.Identifier.ValueText);
                    }
                    break;
                case EventFieldDeclarationSyntax eventFieldDeclaration:
                    foreach (var variable in eventFieldDeclaration.Declaration.Variables)
                    {
                        AddVariableDeclaration(tree, repoId, file, currentNamespace, containingTypes, declarations, eventFieldDeclaration, "event", variable.Identifier.ValueText);
                    }
                    break;
                case EventDeclarationSyntax eventDeclaration:
                    AddMemberDeclaration(tree, repoId, file, currentNamespace, containingTypes, declarations, eventDeclaration, "event", eventDeclaration.Identifier.ValueText, string.Empty);
                    break;
            }
        }
    }

    private static void AddTypeDeclaration(
        SyntaxTree tree,
        string repoId,
        ScannedFile file,
        string currentNamespace,
        IReadOnlyList<string> containingTypes,
        List<SyntaxDeclaration> declarations,
        TypeDeclarationSyntax typeDeclaration)
    {
        var kind = typeDeclaration switch
        {
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            RecordDeclarationSyntax record when record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => "record_struct",
            RecordDeclarationSyntax => "record",
            _ => "type"
        };
        var signature = typeDeclaration.Identifier.ValueText + typeDeclaration.TypeParameterList?.ToString();

        AddDeclaration(tree, repoId, file, currentNamespace, containingTypes, declarations, typeDeclaration, kind, typeDeclaration.Identifier.ValueText, signature, typeDeclaration.Arity);
    }

    private static void AddEnumDeclaration(
        SyntaxTree tree,
        string repoId,
        ScannedFile file,
        string currentNamespace,
        IReadOnlyList<string> containingTypes,
        List<SyntaxDeclaration> declarations,
        EnumDeclarationSyntax enumDeclaration)
    {
        AddDeclaration(tree, repoId, file, currentNamespace, containingTypes, declarations, enumDeclaration, "enum", enumDeclaration.Identifier.ValueText, enumDeclaration.Identifier.ValueText, arity: 0);
    }

    private static void AddDelegateDeclaration(
        SyntaxTree tree,
        string repoId,
        ScannedFile file,
        string currentNamespace,
        IReadOnlyList<string> containingTypes,
        List<SyntaxDeclaration> declarations,
        DelegateDeclarationSyntax delegateDeclaration)
    {
        var signature = $"{delegateDeclaration.Identifier.ValueText}{delegateDeclaration.TypeParameterList}{delegateDeclaration.ParameterList}";
        AddDeclaration(tree, repoId, file, currentNamespace, containingTypes, declarations, delegateDeclaration, "delegate", delegateDeclaration.Identifier.ValueText, signature, delegateDeclaration.Arity);
    }

    private static void AddMemberDeclaration(
        SyntaxTree tree,
        string repoId,
        ScannedFile file,
        string currentNamespace,
        IReadOnlyList<string> containingTypes,
        List<SyntaxDeclaration> declarations,
        MemberDeclarationSyntax syntax,
        string kind,
        string name,
        string suffix)
    {
        var signature = string.IsNullOrWhiteSpace(suffix) ? name : name + suffix;
        AddDeclaration(tree, repoId, file, currentNamespace, containingTypes, declarations, syntax, kind, name, signature, arity: 0);
    }

    private static void AddVariableDeclaration(
        SyntaxTree tree,
        string repoId,
        ScannedFile file,
        string currentNamespace,
        IReadOnlyList<string> containingTypes,
        List<SyntaxDeclaration> declarations,
        MemberDeclarationSyntax syntax,
        string kind,
        string name)
    {
        AddDeclaration(tree, repoId, file, currentNamespace, containingTypes, declarations, syntax, kind, name, name, arity: 0);
    }

    private static void AddDeclaration(
        SyntaxTree tree,
        string repoId,
        ScannedFile file,
        string currentNamespace,
        IReadOnlyList<string> containingTypes,
        List<SyntaxDeclaration> declarations,
        MemberDeclarationSyntax syntax,
        string kind,
        string name,
        string signature,
        int arity)
    {
        var lineSpan = tree.GetLineSpan(syntax.Span);
        var startLine = lineSpan.StartLinePosition.Line + 1;
        var startColumn = lineSpan.StartLinePosition.Character + 1;
        var endLine = lineSpan.EndLinePosition.Line + 1;
        var endColumn = lineSpan.EndLinePosition.Character + 1;
        var containingType = containingTypes.Count == 0 ? null : string.Join(".", containingTypes);
        var fullName = BuildFullName(currentNamespace, containingType, name);
        var signatureHash = Hash($"{kind}|{fullName}|{signature}");
        var syntaxHash = Hash(syntax.ToString());
        var baseId = $"{file.RelativePath}:{startLine}:{startColumn}:{endLine}:{endColumn}:{kind}:{name}:{file.HashInfo.ContentHash[..12]}";
        var declarationId = "decl_" + Hash(baseId)[..16];
        var symbolId = $"csid:v1:{repoId}:syntax:{kind}:{fullName}:{signatureHash[..12]}";

        declarations.Add(new SyntaxDeclaration(
            declarationId,
            symbolId,
            kind,
            name,
            currentNamespace,
            containingType,
            GetAccessibility(syntax.Modifiers),
            string.Join(" ", syntax.Modifiers.Select(modifier => modifier.ValueText)),
            arity,
            signature,
            signatureHash,
            startLine,
            startColumn,
            endLine,
            endColumn,
            syntaxHash));
    }

    private static string BuildFullName(string currentNamespace, string? containingType, string name)
    {
        return string.Join(".", new[] { currentNamespace, containingType, name }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string CombineNamespace(string currentNamespace, string childNamespace)
    {
        return string.IsNullOrWhiteSpace(currentNamespace)
            ? childNamespace
            : currentNamespace + "." + childNamespace;
    }

    private static string? GetAccessibility(SyntaxTokenList modifiers)
    {
        if (modifiers.Any(SyntaxKind.PublicKeyword))
        {
            return "public";
        }

        if (modifiers.Any(SyntaxKind.ProtectedKeyword) && modifiers.Any(SyntaxKind.InternalKeyword))
        {
            return "protected_internal";
        }

        if (modifiers.Any(SyntaxKind.PrivateKeyword) && modifiers.Any(SyntaxKind.ProtectedKeyword))
        {
            return "private_protected";
        }

        if (modifiers.Any(SyntaxKind.ProtectedKeyword))
        {
            return "protected";
        }

        if (modifiers.Any(SyntaxKind.InternalKeyword))
        {
            return "internal";
        }

        if (modifiers.Any(SyntaxKind.PrivateKeyword))
        {
            return "private";
        }

        return null;
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
