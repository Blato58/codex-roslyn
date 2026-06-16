using System.Security.Cryptography;
using System.Text;

namespace Codex.Roslyn.Mcp;

public static class McpHttpSecurity
{
    public static bool IsAllowedHost(string? hostHeader)
    {
        if (string.IsNullOrWhiteSpace(hostHeader))
        {
            return false;
        }

        var host = hostHeader.Split(':', 2)[0].Trim().Trim('[', ']').ToLowerInvariant();
        return host is "127.0.0.1" or "localhost";
    }

    public static bool IsAllowedOrigin(string? originHeader)
    {
        if (string.IsNullOrWhiteSpace(originHeader))
        {
            return true;
        }

        if (!Uri.TryCreate(originHeader, UriKind.Absolute, out var origin))
        {
            return false;
        }

        return origin.Scheme is "http" or "https"
            && (origin.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || origin.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsAuthorized(string? authorizationHeader, string? expectedToken)
    {
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            return true;
        }

        const string prefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suppliedToken = authorizationHeader[prefix.Length..].Trim();
        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedToken);
        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
