using System.Security;
using Microsoft.Win32;
using SysScrub.Core.Cleaning;
using SysScrub.Core.Startup;
using Xunit;

namespace SysScrub.Core.Tests.Startup;

/// <summary>
/// Açma/kapama davranışı.
///
/// En kritik güvence: hiçbir kayıt silinmiyor. Kullanıcı bir öğeyi kapattığında
/// Run değeri yerinde kalmalı ki geri açabilsin.
///
/// Kum havuzu örnek başına ayrı alt anahtar: xUnit test sınıflarını paralel
/// çalıştırıyor, ortak bir kökü silmek yan taraftaki testi düşürür.
/// </summary>
public sealed class StartupManagerTests : IDisposable
{
    private readonly string _sandboxRoot = $@"SOFTWARE\SysScrub.Tests\{Guid.NewGuid():N}";
    private readonly string _historyDirectory =
        Path.Combine(Path.GetTempPath(), "SysScrub.Tests", Guid.NewGuid().ToString("N"));

    private readonly StartupApprovedStore _approvals;
    private readonly HistoryStore _history;
    private readonly StartupManager _manager;

    public StartupManagerTests()
    {
        _approvals = new StartupApprovedStore($@"{_sandboxRoot}\StartupApproved");
        _history = new HistoryStore(_historyDirectory);
        _manager = new StartupManager(_approvals, _history);
    }

    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(_sandboxRoot, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
        }

        try
        {
            if (Directory.Exists(_historyDirectory))
            {
                Directory.Delete(_historyDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
        }
    }

    private static StartupEntry RunEntry(bool enabled = true) => new()
    {
        Id = "reg|CurrentUser|Registry64|Run|Ornek",
        Name = "Ornek",
        Command = @"C:\Program Files\Ornek\ornek.exe",
        Source = StartupSource.RegistryRun,
        IsEnabled = enabled,
        ApprovalScope = StartupApprovedStore.ApprovalScope.Run,
        ApprovalValueName = "Ornek"
    };

    [Fact]
    public async Task KapatmaOnayAnahtarinaYazilir()
    {
        StartupChangeResult result = await _manager.SetEnabledAsync(RunEntry(), enabled: false);

        Assert.True(result.Success);
        Assert.False(_approvals.IsEnabled(
            RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.Run, "Ornek"));
    }

    [Fact]
    public async Task AcmaOnayAnahtarinaYazilir()
    {
        await _manager.SetEnabledAsync(RunEntry(), enabled: false);
        StartupChangeResult result = await _manager.SetEnabledAsync(RunEntry(enabled: false), enabled: true);

        Assert.True(result.Success);
        Assert.True(_approvals.IsEnabled(
            RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.Run, "Ornek"));
    }

    /// <summary>
    /// Bu modülün varlık sebebi: rakiplerin çoğu kaydı siliyor, biz yalnızca
    /// bayrağı değiştiriyoruz. Kayıt silinirse geri alma imkânsız olur.
    /// </summary>
    [Fact]
    public async Task KapatmaKaydiSilmez()
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey($@"{_sandboxRoot}\Run"))
        {
            key.SetValue("Ornek", @"C:\Program Files\Ornek\ornek.exe");
        }

        await _manager.SetEnabledAsync(RunEntry(), enabled: false);

        using RegistryKey? after = Registry.CurrentUser.OpenSubKey($@"{_sandboxRoot}\Run");

        Assert.NotNull(after);
        Assert.Equal(@"C:\Program Files\Ornek\ornek.exe", after!.GetValue("Ornek"));
    }

    [Fact]
    public async Task ServisDegistirilemez()
    {
        var service = new StartupEntry
        {
            Id = "service|OrnekServis",
            Name = "Ornek Servis",
            Command = @"C:\Program Files\Ornek\servis.exe",
            Source = StartupSource.Service,
            IsEnabled = true,
            Control = StartupControl.ReadOnly
        };

        StartupChangeResult result = await _manager.SetEnabledAsync(service, enabled: false);

        Assert.False(result.Success);
        Assert.Contains("Hizmetler", result.Message);
    }

    [Fact]
    public async Task OnayKarsiligiCozulemeyenOgeDegistirilemez()
    {
        StartupEntry broken = RunEntry() with { ApprovalScope = null, ApprovalValueName = null };

        StartupChangeResult result = await _manager.SetEnabledAsync(broken, enabled: false);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task DegisiklikZamanTunelineYazilir()
    {
        await _manager.SetEnabledAsync(RunEntry(), enabled: false);

        HistoryRun run = Assert.Single(_history.ListRuns());

        Assert.Equal(HistoryOperation.StartupChange, run.Operation);
        Assert.Equal(1, run.ItemsAffected);
        Assert.Equal(0, run.BytesFreed);
    }

    [Fact]
    public async Task ZamanTuneliSatiriNeYapildiginiYazar()
    {
        await _manager.SetEnabledAsync(RunEntry(), enabled: false);

        HistoryRun run = Assert.Single(_history.ListRuns());
        HistoryItem item = Assert.Single(_history.ListItems(run.RunId));

        Assert.Equal(HistoryItemOutcome.Changed, item.Outcome);
        Assert.Contains("devre dışı bırakıldı", item.Message);
    }

    /// <summary>Başarısız işlem geçmişe yazılmaz; olmayan bir değişikliği kaydetmiş oluruz.</summary>
    [Fact]
    public async Task BasarisizIslemGecmiseYazilmaz()
    {
        StartupEntry broken = RunEntry() with { ApprovalScope = null, ApprovalValueName = null };

        await _manager.SetEnabledAsync(broken, enabled: false);

        Assert.Empty(_history.ListRuns());
    }
}
