using System.Text;
using SysScrub.Core.Cleaning;
using SysScrub.Core.Machine;
using SysScrub.Core.Rules;
using SysScrub.Core.Safety;
using Xunit;

namespace SysScrub.Core.Tests.Cleaning;

/// <summary>
/// Silme testleri gerçekten dosya siler — hepsi %TEMP% altındaki kendi kum havuzunda.
/// Karantina ve geçmiş de kum havuzuna yönlendirilir, gerçek %ProgramData% kirlenmez.
/// </summary>
public sealed class CleanEngineTests : IDisposable
{
    private readonly string _sandbox;
    private readonly string _sandboxName;
    private readonly QuarantineStore _quarantine;
    private readonly HistoryStore _history;
    private readonly CleanEngine _engine;
    private readonly ScanEngine _scanner;

    public CleanEngineTests()
    {
        _sandboxName = Guid.NewGuid().ToString("N");
        _sandbox = Path.Combine(Path.GetTempPath(), "SysScrub.Tests", _sandboxName);
        Directory.CreateDirectory(_sandbox);

        var resolver = new PathResolver();
        var guard = new SafetyGuard(resolver);

        _quarantine = new QuarantineStore(Path.Combine(_sandbox, "_karantina"));
        _history = new HistoryStore(Path.Combine(_sandbox, "_gecmis"));
        _scanner = new ScanEngine(resolver, guard);
        _engine = new CleanEngine(guard, _quarantine, _history, new SystemInfoService());
    }

    public void Dispose()
    {
        try
        {
            TestJunction.DeleteTree(_sandbox);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ------------------------------------------------------------------ kalıcı silme

    [Fact]
    public async Task KaliciSilmeDosyalariKaldirir()
    {
        WriteFile("cache/a.tmp", 1000);
        WriteFile("cache/b.tmp", 2000);

        CleanResult result = await ScanAndCleanAsync(Rule("cache", DeleteMode.Permanent));

        Assert.Equal(2, result.Deleted);
        Assert.Equal(3000, result.BytesFreed);
        Assert.Empty(result.Failures);
        Assert.False(File.Exists(Path.Combine(_sandbox, "cache", "a.tmp")));
        Assert.False(File.Exists(Path.Combine(_sandbox, "cache", "b.tmp")));
    }

    [Fact]
    public async Task BosalanKlasorlerKaldirilir()
    {
        WriteFile("cache/alt/derin/a.tmp", 100);

        await ScanAndCleanAsync(Rule("cache", DeleteMode.Permanent));

        Assert.False(Directory.Exists(Path.Combine(_sandbox, "cache", "alt", "derin")));
        Assert.False(Directory.Exists(Path.Combine(_sandbox, "cache", "alt")));

        // Kuralın kökü, boşalsa bile silinmez: bir sonraki taramanın hedefi orası.
        Assert.True(Directory.Exists(Path.Combine(_sandbox, "cache")));
    }

    // ------------------------------------------------------------------ karantina turu

    [Fact]
    public async Task KarantinaTuruDosyayiIcerigiylaGeriGetirir()
    {
        const string content = "kaybolmaması gereken içerik — ıöüşçğ";
        string path = Path.Combine(_sandbox, "cache", "onemli.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
        DateTime originalWrite = File.GetLastWriteTimeUtc(path);

        CleanResult result = await ScanAndCleanAsync(Rule("cache", DeleteMode.Quarantine));

        Assert.Equal(1, result.Quarantined);
        Assert.True(result.IsReversible);
        Assert.False(File.Exists(path));

        RestoreResult restore = _quarantine.Restore(result.RunId);

        Assert.True(restore.Succeeded);
        Assert.Equal(1, restore.Restored);
        Assert.True(File.Exists(path));
        Assert.Equal(content, File.ReadAllText(path, Encoding.UTF8));
        Assert.Equal(originalWrite, File.GetLastWriteTimeUtc(path), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task GeriYuklemeMevcutDosyaninUzerineYazmaz()
    {
        string path = Path.Combine(_sandbox, "cache", "a.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "eski");

        CleanResult result = await ScanAndCleanAsync(Rule("cache", DeleteMode.Quarantine));

        // Kullanıcı temizlikten sonra aynı yola yeni bir dosya koydu.
        File.WriteAllText(path, "kullanıcının yeni verisi");

        RestoreResult restore = _quarantine.Restore(result.RunId);

        Assert.Equal(0, restore.Restored);
        Assert.Equal(1, restore.Skipped);
        Assert.Equal("kullanıcının yeni verisi", File.ReadAllText(path));
    }

    [Fact]
    public async Task SaklamaSuresiDolanKarantinaSilinir()
    {
        WriteFile("cache/a.tmp", 100);

        CleanResult result = await ScanAndCleanAsync(Rule("cache", DeleteMode.Quarantine));
        Assert.NotNull(_quarantine.Find(result.RunId));

        _quarantine.Purge(TimeSpan.Zero);

        Assert.Null(_quarantine.Find(result.RunId));
    }

    // ------------------------------------------------------------------ kuru çalıştırma

    [Fact]
    public async Task KuruCalistirmaHicbirSeySilmez()
    {
        WriteFile("cache/a.tmp", 500);

        CleanResult result = await ScanAndCleanAsync(
            Rule("cache", DeleteMode.Permanent),
            new CleanOptions { DryRun = true });

        Assert.True(result.WasDryRun);
        Assert.Equal(500, result.BytesFreed);
        Assert.True(File.Exists(Path.Combine(_sandbox, "cache", "a.tmp")));
        Assert.Empty(_history.ListRuns());
    }

    // ------------------------------------------------------------------ güvenlik

    [Fact]
    public async Task TaramadanSonraDegisenDosyaSilmeAninaDenetlenir()
    {
        WriteFile("cache/a.tmp", 100);

        ScanReport report = await _scanner.ScanAsync(
            new RuleSet([Rule("cache", DeleteMode.Permanent)], []), new ScanOptions());

        // Tarama ile silme arasında öğenin kökü değiştirilirse guard yakalamalı.
        RuleScanResult original = report.Results[0];
        var tampered = original with
        {
            Items = original.Items
                .Select(i => i with { AllowedRoot = Path.Combine(_sandbox, "baska-klasor") })
                .ToArray()
        };

        CleanResult result = await _engine.CleanAsync([tampered], new CleanOptions());

        Assert.Equal(1, result.SkippedByGuard);
        Assert.Equal(0, result.Deleted);
        Assert.True(File.Exists(Path.Combine(_sandbox, "cache", "a.tmp")));
    }

    [Fact]
    public async Task KilitliDosyaSessizceKaybolmaz()
    {
        string path = Path.Combine(_sandbox, "cache", "kilitli.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[100]);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            CleanResult result = await ScanAndCleanAsync(Rule("cache", DeleteMode.Permanent));

            // Ya yeniden başlatmaya işaretlenir (yönetici) ya da başarısız raporlanır.
            // İkisinde de dosya hâlâ yerinde ve kullanıcı durumu görebiliyor olmalı.
            Assert.True(result.ScheduledForReboot + result.Failures.Count > 0);
            Assert.Equal(0, result.Deleted);
            Assert.True(File.Exists(path));
        }
    }

    // ------------------------------------------------------------------ geçmiş

    [Fact]
    public async Task GecmiseKayitYazilir()
    {
        WriteFile("cache/a.tmp", 250);

        CleanResult result = await ScanAndCleanAsync(Rule("cache", DeleteMode.Quarantine));

        HistoryRun run = Assert.Single(_history.ListRuns());

        Assert.Equal(result.RunId, run.RunId);
        Assert.Equal(HistoryOperation.Clean, run.Operation);
        Assert.Equal(250, run.BytesFreed);
        Assert.True(run.IsReversible);

        HistoryItem item = Assert.Single(_history.ListItems(run.RunId));
        Assert.Equal(HistoryItemOutcome.Quarantined, item.Outcome);
    }

    [Fact]
    public async Task GeriAlinanCalistirmaIsaretlenir()
    {
        WriteFile("cache/a.tmp", 100);

        CleanResult result = await ScanAndCleanAsync(Rule("cache", DeleteMode.Quarantine));
        _quarantine.Restore(result.RunId);
        _history.MarkReverted(result.RunId);

        HistoryRun run = Assert.Single(_history.ListRuns());

        Assert.True(run.WasReverted);
        Assert.False(run.IsReversible);
    }

    // ------------------------------------------------------------------ yardımcılar

    private async Task<CleanResult> ScanAndCleanAsync(CleaningRule rule, CleanOptions? options = null)
    {
        ScanReport report = await _scanner.ScanAsync(new RuleSet([rule], []), new ScanOptions());

        return await _engine.CleanAsync(report.WithFindings, options ?? new CleanOptions());
    }

    private CleaningRule Rule(string relativeFolder, DeleteMode deleteMode) => new()
    {
        Id = "test.clean",
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
        DeleteMode = deleteMode
    };

    private void WriteFile(string relativePath, int bytes)
    {
        string full = Path.Combine(_sandbox, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[bytes]);
    }
}
