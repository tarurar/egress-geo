using System.Globalization;

namespace EgressGeo;

internal static class GeoLiteReleaseContract
{
    internal const string Repository = "P3TERX/GeoLite.mmdb";
    internal const string AssetName = "GeoLite2-City.mmdb";

    internal static bool IsDatedTag(string? value) =>
        DateOnly.TryParseExact(
            value,
            "yyyy.MM.dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);

    internal static bool TryParseAssetUrl(
        string? value,
        string tag,
        out Uri assetUrl)
    {
        var expectedPath =
            $"/{Repository}/releases/download/{tag}/{AssetName}";
        var valid = Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            parsed.Scheme == Uri.UriSchemeHttps &&
            string.Equals(parsed.Host, "github.com", StringComparison.Ordinal) &&
            string.Equals(
                parsed.AbsolutePath,
                expectedPath,
                StringComparison.Ordinal) &&
            string.IsNullOrEmpty(parsed.Query) &&
            string.IsNullOrEmpty(parsed.Fragment);
        assetUrl = parsed ?? new Uri("https://github.com");
        return valid;
    }

    internal static bool TryNormalizeDigest(
        string? value,
        out string digest)
    {
        const string prefix = "sha256:";
        var hash = value is not null &&
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? value[prefix.Length..]
                : string.Empty;
        var valid = hash.Length == 64 && hash.All(Uri.IsHexDigit);
        digest = valid
            ? prefix + hash.ToLowerInvariant()
            : string.Empty;
        return valid;
    }

    internal static bool IsValidIdentity(
        string? repository,
        string? tag,
        Uri? assetUrl,
        string? digest) =>
        string.Equals(repository, Repository, StringComparison.Ordinal) &&
        IsDatedTag(tag) &&
        TryParseAssetUrl(assetUrl?.ToString(), tag!, out var parsedUrl) &&
        parsedUrl == assetUrl &&
        TryNormalizeDigest(digest, out var normalizedDigest) &&
        string.Equals(digest, normalizedDigest, StringComparison.Ordinal);
}
