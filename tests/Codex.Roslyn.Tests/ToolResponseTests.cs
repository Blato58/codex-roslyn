using Codex.Roslyn.Abstractions.ToolContracts;

namespace Codex.Roslyn.Tests;

public sealed class ToolResponseTests
{
    [Fact]
    public void Ok_UsesCurrentCacheDefaults()
    {
        var response = ToolResponse<string>.Ok("done", ["item"]);

        Assert.Equal("ok", response.ResultKind);
        Assert.Equal("miss", response.CacheStatus.Index);
        Assert.Equal("cold", response.CacheStatus.Workspace);
        Assert.False(response.TokenPolicy.Truncated);
    }
}
