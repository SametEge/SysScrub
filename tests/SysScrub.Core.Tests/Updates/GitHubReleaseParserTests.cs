using SysScrub.Core.Updates;
using Xunit;

namespace SysScrub.Core.Tests.Updates;

public class GitHubReleaseParserTests
{
    /// <summary>GitHub'ın gerçek yanıtından kırpılmış, kullandığımız alanları taşıyan örnek.</summary>
    private const string SampleJson = """
    [
      {
        "tag_name": "v0.14.0-alpha",
        "name": "SysScrub 0.14.0-alpha",
        "body": "Otomatik güncelleme eklendi.",
        "html_url": "https://github.com/SametEge/SysScrub/releases/tag/v0.14.0-alpha",
        "draft": false,
        "prerelease": true,
        "published_at": "2026-08-14T10:00:00Z",
        "assets": [
          {
            "name": "SysScrub-Setup-0.14.0-alpha.exe",
            "browser_download_url": "https://example.invalid/setup.exe",
            "size": 89123456
          },
          {
            "name": "SHA256SUMS.txt",
            "browser_download_url": "https://example.invalid/SHA256SUMS.txt",
            "size": 240
          }
        ]
      },
      {
        "tag_name": "v0.13.0-alpha",
        "name": "SysScrub 0.13.0-alpha",
        "draft": false,
        "prerelease": true,
        "assets": []
      },
      {
        "tag_name": "v0.15.0",
        "draft": true,
        "prerelease": false,
        "assets": []
      },
      {
        "tag_name": "deneme",
        "draft": false,
        "prerelease": false,
        "assets": []
      }
    ]
    """;

    [Fact]
    public void Taslak_ve_cozumlenemeyen_etiket_atlanir()
    {
        IReadOnlyList<GitHubRelease> releases = GitHubReleaseParser.ParseList(SampleJson);

        Assert.Equal(2, releases.Count);
        Assert.DoesNotContain(releases, r => r.Tag == "deneme");
        Assert.DoesNotContain(releases, r => r.Tag == "v0.15.0");
    }

    [Fact]
    public void Alanlar_dogru_okunur()
    {
        GitHubRelease release = GitHubReleaseParser.ParseList(SampleJson)[0];

        Assert.Equal("v0.14.0-alpha", release.Tag);
        Assert.Equal(new AppVersion(0, 14, 0, "alpha"), release.Version);
        Assert.Equal("SysScrub 0.14.0-alpha", release.Title);
        Assert.Equal("Otomatik güncelleme eklendi.", release.Notes);
        Assert.True(release.IsPreRelease);
        Assert.Equal(2026, release.PublishedAt!.Value.Year);
    }

    [Fact]
    public void Kurulum_paketi_ve_ozet_listesi_eslesir()
    {
        GitHubRelease release = GitHubReleaseParser.ParseList(SampleJson)[0];

        Assert.Equal("SysScrub-Setup-0.14.0-alpha.exe", release.Setup!.Name);
        Assert.Equal(89123456, release.Setup.Size);
        Assert.Equal("SHA256SUMS.txt", release.Checksums!.Name);
    }

    [Fact]
    public void Dosyasiz_yayinda_kurulum_paketi_bulunmaz()
    {
        GitHubRelease release = GitHubReleaseParser.ParseList(SampleJson)[1];

        Assert.Null(release.Setup);
        Assert.Null(release.Checksums);
    }

    [Fact]
    public void Tek_nesne_donen_yanit_da_okunur()
    {
        const string single = """
        { "tag_name": "v1.0.0", "draft": false, "prerelease": false, "assets": [] }
        """;

        IReadOnlyList<GitHubRelease> releases = GitHubReleaseParser.ParseList(single);

        Assert.Single(releases);
        Assert.Equal(new AppVersion(1, 0, 0), releases[0].Version);
    }

    [Fact]
    public void Bos_dizi_bos_liste_verir()
    {
        Assert.Empty(GitHubReleaseParser.ParseList("[]"));
    }
}
