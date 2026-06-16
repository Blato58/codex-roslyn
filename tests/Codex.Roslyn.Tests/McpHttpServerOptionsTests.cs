using Codex.Roslyn.Mcp;

namespace Codex.Roslyn.Tests;

public sealed class McpHttpServerOptionsTests
{
    [Fact]
    public void FromArgs_UsesLoopbackDefaults()
    {
        var options = McpHttpServerOptions.FromArgs(["--http"]);

        Assert.Equal(38777, options.Port);
        Assert.Equal("/mcp", options.Path);
        Assert.Null(options.BearerToken);
    }

    [Fact]
    public void FromArgs_ParsesPortPathAndToken()
    {
        var options = McpHttpServerOptions.FromArgs(["--http", "--port", "39001", "--path", "roslyn", "--token", "secret"]);

        Assert.Equal(39001, options.Port);
        Assert.Equal("/roslyn", options.Path);
        Assert.Equal("secret", options.BearerToken);
    }

    [Theory]
    [InlineData("127.0.0.1:38777", true)]
    [InlineData("localhost:38777", true)]
    [InlineData("example.com:38777", false)]
    public void IsAllowedHost_OnlyAllowsLoopbackNames(string host, bool expected)
    {
        Assert.Equal(expected, McpHttpSecurity.IsAllowedHost(host));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("http://127.0.0.1:38777", true)]
    [InlineData("http://localhost:38777", true)]
    [InlineData("https://example.com", false)]
    [InlineData("not a uri", false)]
    public void IsAllowedOrigin_AllowsAbsentOrLoopbackOrigins(string? origin, bool expected)
    {
        Assert.Equal(expected, McpHttpSecurity.IsAllowedOrigin(origin));
    }

    [Fact]
    public void IsAuthorized_RequiresBearerOnlyWhenTokenIsConfigured()
    {
        Assert.True(McpHttpSecurity.IsAuthorized(null, null));
        Assert.True(McpHttpSecurity.IsAuthorized("Bearer secret", "secret"));
        Assert.False(McpHttpSecurity.IsAuthorized("Bearer wrong", "secret"));
        Assert.False(McpHttpSecurity.IsAuthorized(null, "secret"));
    }
}
