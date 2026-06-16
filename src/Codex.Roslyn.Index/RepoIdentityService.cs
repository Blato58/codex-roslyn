using System.Security.Cryptography;
using System.Text;

namespace Codex.Roslyn.Index;

public sealed class RepoIdentityService
{
    public RepoIdentity Create(string repoRoot)
    {
        var normalizedRoot = Path.GetFullPath(repoRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var remoteUrl = ReadOriginRemoteUrl(normalizedRoot);
        var input = $"{normalizedRoot.ToLowerInvariant()}|{remoteUrl?.ToLowerInvariant() ?? string.Empty}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var repoId = "repo_" + Convert.ToHexString(bytes)[..16].ToLowerInvariant();

        return new RepoIdentity(normalizedRoot, repoId, remoteUrl);
    }

    private static string? ReadOriginRemoteUrl(string repoRoot)
    {
        var configPath = Path.Combine(repoRoot, ".git", "config");
        if (!File.Exists(configPath))
        {
            return null;
        }

        var inOrigin = false;
        foreach (var rawLine in File.ReadLines(configPath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inOrigin = line.Equals("[remote \"origin\"]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inOrigin && line.StartsWith("url", StringComparison.OrdinalIgnoreCase))
            {
                var separator = line.IndexOf('=');
                if (separator >= 0)
                {
                    return line[(separator + 1)..].Trim();
                }
            }
        }

        return null;
    }
}
