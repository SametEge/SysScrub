using System.Text.Json;

namespace SysScrub.Core.Updates;

/// <summary>Bir yayına eklenmiş tek dosya.</summary>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

/// <summary>
/// GitHub Releases'taki tek bir yayın.
///
/// Yalnızca kullandığımız alanlar tutuluyor; API'nin geri kalanı ilgilendirmiyor.
/// </summary>
public sealed record GitHubRelease
{
    public required string Tag { get; init; }

    public required AppVersion Version { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public string PageUrl { get; init; } = string.Empty;

    public DateTimeOffset? PublishedAt { get; init; }

    public bool IsPreRelease { get; init; }

    public IReadOnlyList<ReleaseAsset> Assets { get; init; } = [];

    /// <summary>Kurulum paketi. Yoksa güncelleme indirilemez, yalnızca sayfası açılır.</summary>
    public ReleaseAsset? Setup => Assets.FirstOrDefault(asset =>
        asset.Name.StartsWith("SysScrub-Setup-", StringComparison.OrdinalIgnoreCase) &&
        asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

    /// <summary>SHA256 listesi; indirileni doğrulamak için.</summary>
    public ReleaseAsset? Checksums => Assets.FirstOrDefault(asset =>
        asset.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// GitHub'ın yayın JSON'unu çözümler.
///
/// Ayrı sınıf, çünkü ağ olmadan test edilebilmesi gerekiyor: kaydedilmiş bir
/// yanıt üzerinde sürüm sıralaması ve dosya eşlemesi doğrulanabiliyor.
/// </summary>
public static class GitHubReleaseParser
{
    /// <summary>
    /// Taslaklar ve sürüm numarası çözümlenemeyen etiketler atlanır — elle
    /// oluşturulmuş "test" etiketleri yüzünden güncelleme önerilmesin.
    /// </summary>
    public static IReadOnlyList<GitHubRelease> ParseList(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            GitHubRelease? single = ParseRelease(document.RootElement);

            return single is null ? [] : [single];
        }

        var releases = new List<GitHubRelease>();

        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            if (ParseRelease(element) is { } release)
            {
                releases.Add(release);
            }
        }

        return releases;
    }

    private static GitHubRelease? ParseRelease(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (Bool(element, "draft"))
        {
            return null;
        }

        string tag = String(element, "tag_name");

        if (!AppVersion.TryParse(tag, out AppVersion version))
        {
            return null;
        }

        return new GitHubRelease
        {
            Tag = tag,
            Version = version,
            Title = String(element, "name"),
            Notes = String(element, "body"),
            PageUrl = String(element, "html_url"),
            PublishedAt = Timestamp(element, "published_at"),
            IsPreRelease = Bool(element, "prerelease"),
            Assets = ParseAssets(element)
        };
    }

    private static IReadOnlyList<ReleaseAsset> ParseAssets(JsonElement element)
    {
        if (!element.TryGetProperty("assets", out JsonElement assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<ReleaseAsset>();

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string name = String(asset, "name");
            string url = String(asset, "browser_download_url");

            if (name.Length == 0 || url.Length == 0)
            {
                continue;
            }

            long size = asset.TryGetProperty("size", out JsonElement sizeElement) &&
                        sizeElement.TryGetInt64(out long parsed)
                ? parsed
                : 0;

            result.Add(new ReleaseAsset(name, url, size));
        }

        return result;
    }

    private static string String(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? Timestamp(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String &&
        value.TryGetDateTimeOffset(out DateTimeOffset parsed)
            ? parsed
            : null;
}
