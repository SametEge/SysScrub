using System.Security;
using Microsoft.Win32;
using SysScrub.Core.Startup;
using Xunit;

namespace SysScrub.Core.Tests.Startup;

/// <summary>
/// Onay anahtarının bayt biçimi testleri.
///
/// Bu biçimi Windows tanımlıyor; yanlış yazarsak Görev Yöneticisi öğeyi bizden
/// farklı görür ve iki araç birbirini ezer. Testler gerçek anahtara değil,
/// HKCU\Software\SysScrub.Tests altındaki kendi kum havuzuna yazıyor —
/// örnek başına ayrı alt anahtar, çünkü xUnit test sınıflarını paralel çalıştırıyor.
/// </summary>
public sealed class StartupApprovedStoreTests : IDisposable
{
    private readonly string _sandboxRoot = $@"SOFTWARE\SysScrub.Tests\{Guid.NewGuid():N}";
    private readonly string _approvalsPath;
    private readonly StartupApprovedStore _store;

    public StartupApprovedStoreTests()
    {
        _approvalsPath = $@"{_sandboxRoot}\StartupApproved";
        _store = new StartupApprovedStore(_approvalsPath);
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
    }

    [Fact]
    public void KayitYoksaOgeAcikSayilir() =>
        Assert.True(_store.IsEnabled(
            RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.Run, "HicYazilmamis"));

    [Fact]
    public void KapatilanOgeKapaliOkunur()
    {
        Assert.True(_store.SetEnabled(
            RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.Run, "Ornek", enabled: false));

        Assert.False(_store.IsEnabled(
            RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.Run, "Ornek"));
    }

    [Fact]
    public void TekrarAcilanOgeAcikOkunur()
    {
        _store.SetEnabled(RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.Run, "Ornek", false);
        _store.SetEnabled(RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.Run, "Ornek", true);

        Assert.True(_store.IsEnabled(
            RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.Run, "Ornek"));
    }

    [Fact]
    public void KapatmaOnIkiBaytlikIkiliDegerYazar()
    {
        _store.SetEnabled(RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.Run, "Ornek", false);

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{_approvalsPath}\Run");
        var value = key?.GetValue("Ornek") as byte[];

        Assert.NotNull(value);
        Assert.Equal(12, value!.Length);
        Assert.Equal(0x03, value[0]);
        Assert.Equal(RegistryValueKind.Binary, key!.GetValueKind("Ornek"));
    }

    [Fact]
    public void AcmaIlkBaytiIkiYapar()
    {
        _store.SetEnabled(RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.Run, "Ornek", true);

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey($@"{_approvalsPath}\Run");
        var value = key?.GetValue("Ornek") as byte[];

        Assert.NotNull(value);
        Assert.Equal(0x02, value![0]);
    }

    [Fact]
    public void KapatmaZamaniKaydedilir()
    {
        DateTime before = DateTime.Now.AddSeconds(-5);

        _store.SetEnabled(RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.Run, "Ornek", false);

        DateTime? disabledAt = _store.DisabledAt(
            RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.Run, "Ornek");

        Assert.NotNull(disabledAt);
        Assert.InRange(disabledAt!.Value, before, DateTime.Now.AddSeconds(5));
    }

    [Fact]
    public void AcikOgeninKapatilmaZamaniYok()
    {
        _store.SetEnabled(RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.Run, "Ornek", true);

        Assert.Null(_store.DisabledAt(
            RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.Run, "Ornek"));
    }

    /// <summary>
    /// Görev Yöneticisi kimi sürümlerde 0x06/0x07 yazıyor. Durum bitine bakıyoruz,
    /// tam eşitliğe değil; yoksa Windows'un açık bıraktığı öğeyi kapalı sanardık.
    /// </summary>
    [Theory]
    [InlineData(0x02, true)]
    [InlineData(0x06, true)]
    [InlineData(0x03, false)]
    [InlineData(0x07, false)]
    public void WindowsunYazdigiTumDurumBaytlariAnlasilir(byte flag, bool expected)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey($@"{_approvalsPath}\Run"))
        {
            var state = new byte[12];
            state[0] = flag;
            key.SetValue("Ornek", state, RegistryValueKind.Binary);
        }

        Assert.Equal(expected, _store.IsEnabled(
            RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.Run, "Ornek"));
    }

    [Fact]
    public void KapsamlarAyriAnahtarlaraYazilir()
    {
        _store.SetEnabled(RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.Run, "Ornek", false);

        // Aynı ad başka kapsamda kapatılmış sayılmamalı.
        Assert.True(_store.IsEnabled(
            RegistryHive.CurrentUser, StartupApprovedStore.ApprovalScope.StartupFolder, "Ornek"));
    }
}
