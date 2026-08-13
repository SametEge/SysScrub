using SysScrub.Core.Updates;
using Xunit;

namespace SysScrub.Core.Tests.Updates;

public class UpdateEvaluationTests
{
    private static GitHubRelease Release(string tag, bool preRelease, bool withSetup = true)
    {
        AppVersion version = AppVersion.Parse(tag);

        return new GitHubRelease
        {
            Tag = tag,
            Version = version,
            IsPreRelease = preRelease,
            Assets = withSetup
                ?
                [
                    new ReleaseAsset($"SysScrub-Setup-{version}.exe", "https://example.invalid/setup.exe", 100),
                    new ReleaseAsset("SHA256SUMS.txt", "https://example.invalid/sums.txt", 64)
                ]
                : []
        };
    }

    [Fact]
    public void Eski_yayinlar_guncelleme_sayilmaz()
    {
        UpdateCheckResult result = UpdateService.Evaluate(
            [Release("v0.12.0", false), Release("v0.13.0", false)],
            AppVersion.Parse("0.13.0"));

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.Null(result.Release);
    }

    [Fact]
    public void En_yeni_surum_secilir()
    {
        UpdateCheckResult result = UpdateService.Evaluate(
            [Release("v1.0.0", false), Release("v1.2.0", false), Release("v1.1.0", false)],
            AppVersion.Parse("1.0.0"));

        Assert.Equal(UpdateStatus.Available, result.Status);
        Assert.Equal("v1.2.0", result.Release!.Tag);
    }

    /// <summary>Kararlı sürüm kullanan kişi ön yayına sürüklenmemeli.</summary>
    [Fact]
    public void Kararli_surumde_on_yayinlar_atlanir()
    {
        UpdateCheckResult result = UpdateService.Evaluate(
            [Release("v1.1.0-beta", true)],
            AppVersion.Parse("1.0.0"));

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    /// <summary>Alfa kullanan kişi de alfada mahsur kalmamalı.</summary>
    [Fact]
    public void On_yayinda_on_yayinlar_onerilir()
    {
        UpdateCheckResult result = UpdateService.Evaluate(
            [Release("v0.14.0-alpha", true)],
            AppVersion.Parse("0.13.0-alpha"));

        Assert.Equal(UpdateStatus.Available, result.Status);
        Assert.Equal("v0.14.0-alpha", result.Release!.Tag);
    }

    [Fact]
    public void On_yayindan_kararli_surume_gecilebilir()
    {
        UpdateCheckResult result = UpdateService.Evaluate(
            [Release("v0.13.0", false)],
            AppVersion.Parse("0.13.0-alpha"));

        Assert.Equal(UpdateStatus.Available, result.Status);
        Assert.Equal("v0.13.0", result.Release!.Tag);
    }

    [Fact]
    public void Kurulum_paketi_yoksa_ayri_durum_donuyor()
    {
        UpdateCheckResult result = UpdateService.Evaluate(
            [Release("v2.0.0", false, withSetup: false)],
            AppVersion.Parse("1.0.0"));

        Assert.Equal(UpdateStatus.AvailableWithoutSetup, result.Status);
        Assert.Equal("v2.0.0", result.Release!.Tag);
    }

    [Fact]
    public void Bos_liste_guncel_sayilir()
    {
        Assert.Equal(UpdateStatus.UpToDate, UpdateService.Evaluate([], AppVersion.Parse("1.0.0")).Status);
    }

    [Fact]
    public void On_yayin_tercihi_elle_zorlanabilir()
    {
        UpdateCheckResult result = UpdateService.Evaluate(
            [Release("v1.1.0-beta", true)],
            AppVersion.Parse("1.0.0"),
            includePreRelease: true);

        Assert.Equal(UpdateStatus.Available, result.Status);
    }
}
