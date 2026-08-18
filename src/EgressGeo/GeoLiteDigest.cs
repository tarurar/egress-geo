using System.Security.Cryptography;

namespace EgressGeo;

internal static class GeoLiteDigest
{
    internal static async ValueTask<string> Compute(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous |
                    FileOptions.SequentialScan,
            });
        var digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return "sha256:" + Convert.ToHexStringLower(digest);
    }
}
