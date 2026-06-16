using Codex.Roslyn.Core;

namespace Codex.Roslyn.Tests;

public sealed class BashGuardServiceTests
{
    [Fact]
    public void EvaluateHookInput_WarnsForBroadCSharpSourceDump()
    {
        var service = new BashGuardService();
        var result = service.EvaluateHookInput(
            """
            {
              "hookEventName": "PreToolUse",
              "toolName": "Bash",
              "toolInput": {
                "command": "find . -name '*.cs' -print -exec cat {} ;"
              }
            }
            """);

        Assert.True(result.HasWarning);
        Assert.Contains("Avoid broad C# source dumps", result.AdditionalContext);
    }

    [Fact]
    public void EvaluateHookInput_AllowsOrdinaryBuildCommandWithoutWarning()
    {
        var service = new BashGuardService();
        var result = service.EvaluateHookInput("""{"toolInput":{"command":"dotnet build CodexRoslyn.slnx --no-restore"}}""");

        Assert.False(result.HasWarning);
        Assert.Null(result.AdditionalContext);
    }

    [Fact]
    public void EvaluateHookInput_WarnsForBroadSymbolSearch()
    {
        var service = new BashGuardService();
        var result = service.EvaluateHookInput("""{"toolInput":{"command":"rg CustomerService"}}""");

        Assert.True(result.HasWarning);
        Assert.Contains("prefer cs_symbol_search", result.AdditionalContext);
    }

    [Fact]
    public void EvaluateHookInput_WarnsForBroadDotnetTestCommand()
    {
        var service = new BashGuardService();
        var result = service.EvaluateHookInput("""{"toolInput":{"command":"dotnet test --no-restore"}}""");

        Assert.True(result.HasWarning);
        Assert.Contains("cs_test_impact", result.AdditionalContext);
    }

    [Fact]
    public void EvaluateHookInput_AllowsTargetedDotnetTestCommand()
    {
        var service = new BashGuardService();
        var result = service.EvaluateHookInput("""{"toolInput":{"command":"dotnet test tests/Sample.Tests/Sample.Tests.csproj --filter FullyQualifiedName~SampleTests"}}""");

        Assert.False(result.HasWarning);
        Assert.Null(result.AdditionalContext);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{not json")]
    public void EvaluateHookInput_FailsOpenForEmptyOrMalformedInput(string hookInput)
    {
        var service = new BashGuardService();
        var result = service.EvaluateHookInput(hookInput);

        Assert.False(result.HasWarning);
        Assert.Null(result.AdditionalContext);
    }
}
