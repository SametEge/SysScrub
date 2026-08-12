using SysScrub.Core.Cleaning;
using SysScrub.Core.Rules;
using SysScrub.Core.Safety;
using Xunit;

namespace SysScrub.Core.Tests.Cleaning;

/// <summary>
/// Tarama testleri gerçek dosya sistemi üzerinde çalışır. Kural kökleri %TEMP% altındaki
/// bir kum havuzuna yönlendirilir; böylece PathResolver ve SafetyGuard da devrede olur
/// ve testler gerçek yolu baştan sona doğrular.
/// </summary>
public sealed class ScanEngineTests : IDisposable
{
    private readonly string _sandbox;
    private readonly string _sandboxName;
    private readonly ScanEngine _engine;

    public ScanEngineTests()
    {
        _sandboxName = Guid.NewGuid().ToString("N");
        _sandbox = Path.Combine(Path.GetTempPath(), "SysScrub.Tests", _sandboxName);
        Directory.CreateDirectory(_sandbox);

        var resolver = new PathResolver();
        _engine = new ScanEngine(resolver, new SafetyGuard(resolver));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ------------------------------------------------------------------ temel tarama

    [Fact]
    public async Task EslesenDosyalarBulunurVeBoyutToplanir()
    {
        WriteFile("cache/a.tmp", 1000);
        WriteFile("cache/b.tmp", 2000);
        WriteFile("cache/alt/c.tmp", 500);

        ScanReport report = await ScanAsync(Rule("cache"));

        Assert.Equal(3, report.TotalCount);
        Assert.Equal(3500, report.TotalBytes);
    }

    [Fact]
    public async Task HedefYoksaBulguOlmaz()
    {
        ScanReport report = await ScanAsync(Rule("olmayan-klasor"));

        Assert.Empty(report.WithFindings);
        Assert.True(report.Results[0].NoTargets);
    }

    [Fact]
    public async Task AltKlasorlereInmeyenKuralYalnizcaKokuTarar()
    {
        WriteFile("cache/kok.tmp", 100);
        WriteFile("cache/alt/derin.tmp", 100);

        ScanReport report = await ScanAsync(Rule("cache", recursive: false));

        Assert.Single(report.Results[0].Items);
        Assert.Equal("kok.tmp", report.Results[0].Items[0].FileName);
    }

    // ------------------------------------------------------------------ süzgeçler

    [Fact]
    public async Task IceAlmaDeseniDisindakilerAtlanir()
    {
        WriteFile("cache/a.log", 100);
        WriteFile("cache/b.tmp", 100);

        ScanReport report = await ScanAsync(Rule("cache", include: ["**/*.log"]));

        Assert.Single(report.Results[0].Items);
        Assert.Equal("a.log", report.Results[0].Items[0].FileName);
    }

    [Fact]
    public async Task DisAlmaDeseniIceAlmayiEzer()
    {
        WriteFile("cache/a.tmp", 100);
        WriteFile("cache/kilit.lock", 100);

        ScanReport report = await ScanAsync(Rule("cache", exclude: ["**/*.lock"]));

        Assert.Single(report.Results[0].Items);
        Assert.Equal("a.tmp", report.Results[0].Items[0].FileName);
    }

    [Fact]
    public async Task YeniDosyalarYasSuzgeciyleKorunur()
    {
        // Kullanımdaki geçici dosyaların altından çekmemek için minAgeDays var.
        WriteFile("cache/yeni.tmp", 100);
        WriteFile("cache/eski.tmp", 100, ageDays: 10);

        ScanReport report = await ScanAsync(Rule("cache", minAgeDays: 3));

        Assert.Single(report.Results[0].Items);
        Assert.Equal("eski.tmp", report.Results[0].Items[0].FileName);
    }

    // ------------------------------------------------------------------ kural seçimi

    [Fact]
    public async Task KapaliKuralTaranmaz()
    {
        WriteFile("cache/a.tmp", 100);

        ScanReport report = await ScanAsync(
            Rule("cache"),
            new ScanOptions { EnabledRuleIds = new HashSet<string>() });

        Assert.Empty(report.Results);
    }

    [Fact]
    public async Task YoneticiGerektirenKuralYetkiYokkenAtlanir()
    {
        WriteFile("cache/a.tmp", 100);

        ScanReport report = await ScanAsync(
            Rule("cache", requiresAdmin: true),
            new ScanOptions { IsElevated = false });

        Assert.Empty(report.Results);
        Assert.Equal(1, report.SkippedForElevation);
    }

    // ------------------------------------------------------------------ güvenlik

    [Fact]
    public async Task BaglantiNoktasininIcineGirilmez()
    {
        // Bağlantı izlenirse hedefteki dosyalar da silinecekler listesine girer.
        string target = Path.Combine(_sandbox, "hedef");
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "degerli.dat"), new byte[100]);

        Directory.CreateDirectory(Path.Combine(_sandbox, "cache"));
        WriteFile("cache/normal.tmp", 100);

        if (!TestJunction.TryCreate(Path.Combine(_sandbox, "cache", "link"), target))
        {
            return;
        }

        try
        {
            ScanReport report = await ScanAsync(Rule("cache"));

            Assert.Single(report.Results[0].Items);
            Assert.Equal("normal.tmp", report.Results[0].Items[0].FileName);
        }
        finally
        {
            Directory.Delete(Path.Combine(_sandbox, "cache", "link"), recursive: false);
        }
    }

    [Fact]
    public async Task BulunanOgelerIzinliKokuTasir()
    {
        // Silme anında SafetyGuard'a aynı kök verilecek; taşınmazsa denetim yapılamaz.
        WriteFile("cache/a.tmp", 100);

        ScanReport report = await ScanAsync(Rule("cache"));

        ScanItem item = report.Results[0].Items[0];
        Assert.False(string.IsNullOrEmpty(item.AllowedRoot));
        Assert.True(PathResolver.IsUnder(item.Path, item.AllowedRoot));
    }

    // ------------------------------------------------------------------ ilerleme ve iptal

    [Fact]
    public async Task IlerlemeRaporlanir()
    {
        WriteFile("cache/a.tmp", 1000);

        var reports = new List<ScanProgress>();
        var progress = new Progress<ScanProgress>(reports.Add);

        await _engine.ScanAsync(BuildSet(Rule("cache")), new ScanOptions(), progress);

        // Progress<T> geri çağırmaları eşzamanlamaya gönderiliyor; testte bir tur bekliyoruz.
        await Task.Delay(50);

        Assert.NotEmpty(reports);
        Assert.Equal(1, reports[^1].TotalRules);
    }

    [Fact]
    public async Task IptalEdilebilir()
    {
        for (int i = 0; i < 200; i++)
        {
            WriteFile($"cache/dosya{i}.tmp", 10);
        }

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _engine.ScanAsync(BuildSet(Rule("cache")), new ScanOptions(), null, cts.Token));
    }

    // ------------------------------------------------------------------ yardımcılar

    private Task<ScanReport> ScanAsync(CleaningRule rule, ScanOptions? options = null) =>
        _engine.ScanAsync(BuildSet(rule), options ?? new ScanOptions());

    private static RuleSet BuildSet(CleaningRule rule) => new([rule], []);

    private CleaningRule Rule(
        string relativeFolder,
        string[]? include = null,
        string[]? exclude = null,
        int minAgeDays = 0,
        bool recursive = true,
        bool requiresAdmin = false)
    {
        return new CleaningRule
        {
            Id = "test.rule",
            Category = RuleCategory.Other,
            Group = "Test",
            Name = LocalizedText.FromSingle("Test kuralı"),
            Roots =
            [
                new RuleRoot
                {
                    Base = PathToken.UserTemp,
                    Path = $"SysScrub.Tests/{_sandboxName}/{relativeFolder}"
                }
            ],
            Include = include is null ? [GlobPattern.MatchAll] : include.Select(GlobPattern.Parse).ToArray(),
            Exclude = exclude?.Select(GlobPattern.Parse).ToArray() ?? [],
            MinAgeDays = minAgeDays,
            Recursive = recursive,
            RequiresAdmin = requiresAdmin,
            DeleteMode = DeleteMode.Permanent
        };
    }

    private void WriteFile(string relativePath, int bytes, int ageDays = 0)
    {
        string full = Path.Combine(_sandbox, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[bytes]);

        if (ageDays > 0)
        {
            File.SetLastWriteTimeUtc(full, DateTime.UtcNow.AddDays(-ageDays));
        }
    }
}
