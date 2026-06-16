using Microsoft.CodeAnalysis;

namespace Codex.Roslyn.Core;

public sealed class SemanticSymbolCache
{
    private readonly object sync = new();
    private readonly Dictionary<string, ISymbol> symbols = new(StringComparer.Ordinal);

    public void Store(string symbolId, ISymbol symbol)
    {
        lock (sync)
        {
            symbols[symbolId] = symbol;
        }
    }

    public bool TryGet(string symbolId, out ISymbol symbol)
    {
        lock (sync)
        {
            return symbols.TryGetValue(symbolId, out symbol!);
        }
    }
}
