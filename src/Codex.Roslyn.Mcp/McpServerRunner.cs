using Codex.Roslyn.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Codex.Roslyn.Mcp;

public static class McpServerRunner
{
    public static Task RunStdioAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services.AddCodexRoslynServices();
        builder.Services.AddSingleton(WorkspaceEditOptions.FromEnvironmentAndArgs(args));
        builder.Services.AddHostedService<ColdIndexWatcher>();
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new()
                {
                    Name = "codex-roslyn",
                    Version = "0.1.0-preview.20260617"
                };
                options.ServerInstructions = Instructions.Text;
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        return builder.Build().RunAsync(cancellationToken);
    }

    public static Task RunHttpAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var options = McpHttpServerOptions.FromArgs(args);
        var builder = WebApplication.CreateBuilder(args);

        builder.Logging.AddConsole();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, options.Port);
        });

        builder.Services.AddCodexRoslynServices();
        builder.Services.AddSingleton(WorkspaceEditOptions.FromEnvironmentAndArgs(args));
        builder.Services.AddHostedService<ColdIndexWatcher>();
        builder.Services
            .AddMcpServer(serverOptions =>
            {
                serverOptions.ServerInfo = new()
                {
                    Name = "codex-roslyn",
                    Version = "0.1.0-preview.20260617"
                };
                serverOptions.ServerInstructions = Instructions.Text;
            })
            .WithHttpTransport(httpOptions =>
            {
                httpOptions.Stateless = false;
                httpOptions.IdleTimeout = TimeSpan.FromMinutes(30);
                httpOptions.MaxIdleSessionCount = 128;
            })
            .WithToolsFromAssembly();

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (!McpHttpSecurity.IsAllowedHost(context.Request.Host.Value))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Host header must be localhost or 127.0.0.1.", cancellationToken);
                return;
            }

            if (!McpHttpSecurity.IsAllowedOrigin(context.Request.Headers.Origin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Origin header is not allowed.", cancellationToken);
                return;
            }

            if (!McpHttpSecurity.IsAuthorized(context.Request.Headers.Authorization, options.BearerToken))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Bearer token is required.", cancellationToken);
                return;
            }

            await next(context);
        });

        app.MapGet("/healthz", () => Results.Ok(new
        {
            status = "ok",
            transport = "streamable_http",
            endpoint = options.Path
        }));
        app.MapMcp(options.Path);

        return app.RunAsync(cancellationToken);
    }
}
