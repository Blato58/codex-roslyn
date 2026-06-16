using System.Security.Cryptography;

namespace Codex.Roslyn.Index;

public sealed class FileHasher
{
    public FileHashInfo Hash(string path)
    {
        var info = new FileInfo(path);
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);

        return new FileHashInfo(
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            Convert.ToHexString(hash).ToLowerInvariant());
    }
}
