using Codex.Roslyn.Index;
using Codex.Roslyn.Workspaces;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Roslyn.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCodexRoslynPhaseZero(this IServiceCollection services)
    {
        services.AddSingleton<RepoIdentityService>();
        services.AddSingleton<IndexPathProvider>();
        services.AddSingleton<FileHasher>();
        services.AddSingleton<RepoScanner>();
        services.AddSingleton<SyntaxDeclarationIndexer>();
        services.AddSingleton<IndexDatabase>();
        services.AddSingleton<ColdIndexService>();
        services.AddSingleton<RepoRootResolver>();
        services.AddSingleton<SolutionDiscoveryService>();
        services.AddSingleton<RepoOverviewService>();
        services.AddSingleton<IndexStatusService>();
        services.AddSingleton<SymbolSearchService>();
        services.AddSingleton<DocumentOutlineService>();
        services.AddSingleton<SolutionSelectionService>();
        services.AddSingleton<SemanticSymbolIdService>();
        services.AddSingleton<SemanticSymbolCache>();
        services.AddSingleton<WorkspaceManager>();
        services.AddSingleton<SemanticQueryService>();
        services.AddSingleton<BashGuardService>();
        services.AddSingleton<RefactorPreviewService>();

        return services;
    }
}
