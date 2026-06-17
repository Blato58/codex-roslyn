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

    [Theory]
    [InlineData("""{"toolInput":{"command":"grep -R CustomerService ."}}""")]
    [InlineData("""{"toolInput":{"command":"Select-String -Path **/*.cs -Pattern CustomerService"}}""")]
    [InlineData("""{"toolInput":{"command":"Get-ChildItem -Recurse -Filter *.cs | Select-String CustomerService"}}""")]
    public void EvaluateHookInput_WarnsForBroadCSharpShellSearch(string hookInput)
    {
        var service = new BashGuardService();
        var result = service.EvaluateHookInput(hookInput);

        Assert.True(result.HasWarning);
        Assert.Contains("cs_symbol_search", result.AdditionalContext);
        Assert.Contains("cs_find_references", result.AdditionalContext);
    }

    [Fact]
    public void EvaluateHookInput_WarnsForPowerShellCSharpSourceDump()
    {
        var service = new BashGuardService();
        var result = service.EvaluateHookInput("""{"toolInput":{"command":"Get-ChildItem -Recurse -Filter *.cs | Get-Content"}}""");

        Assert.True(result.HasWarning);
        Assert.Contains("Avoid broad C# source dumps", result.AdditionalContext);
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
    [InlineData("""{"toolInput":{"command":"Get-Content src/Sample/Foo.cs"}}""")]
    [InlineData("""{"toolInput":{"command":"Get-ChildItem src/Sample"}}""")]
    [InlineData("""{"toolInput":{"command":"rg --files -g *.cs"}}""")]
    public void EvaluateHookInput_AllowsNarrowFileInspectionWithoutWarning(string hookInput)
    {
        var service = new BashGuardService();
        var result = service.EvaluateHookInput(hookInput);

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
