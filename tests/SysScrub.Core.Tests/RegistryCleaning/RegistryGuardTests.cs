using Microsoft.Win32;
using SysScrub.Core.RegistryCleaning;
using Xunit;

namespace SysScrub.Core.Tests.RegistryCleaning;

/// <summary>
/// SafetyGuard testleriyle aynı ağırlıkta: buradaki bir gerileme, Windows'u
/// açılmaz hale getirebilecek bir silme demek.
/// </summary>
public sealed class RegistryGuardTests
{
    private readonly RegistryGuard _guard = new();

    private static RegistryLocation Value(RegistryHive hive, string keyPath, string valueName = "Deger") =>
        new() { Hive = hive, KeyPath = keyPath, ValueName = valueName };

    private static RegistryLocation Key(RegistryHive hive, string keyPath) =>
        new() { Hive = hive, KeyPath = keyPath };

    // ------------------------------------------------------------------ izin verilenler

    [Theory]
    [InlineData(@"SOFTWARE\Classes\.olmayanuzanti")]
    [InlineData(@"SOFTWARE\Classes\CLSID\{00000000-0000-0000-0000-000000000000}")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\olmayan.exe")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OlmayanUygulama")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved")]
    public void IzinliKapsamdakiDegerlereIzinVerilir(string keyPath) =>
        Assert.True(_guard.Inspect(Value(RegistryHive.LocalMachine, keyPath)).IsAllowed, keyPath);

    [Theory]
    [InlineData(@"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache")]
    [InlineData(@"AppEvents\Schemes\Apps\OlmayanUygulama")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")]
    public void KullaniciKovanindakiIzinliDallaraIzinVerilir(string keyPath) =>
        Assert.True(_guard.Inspect(Value(RegistryHive.CurrentUser, keyPath)).IsAllowed, keyPath);

    [Fact]
    public void IzinliKapsamAltindakiAnahtarSilinebilir() =>
        Assert.True(_guard.Inspect(
            Key(RegistryHive.LocalMachine, @"SOFTWARE\Classes\CLSID\{ABC}")).IsAllowed);

    // ------------------------------------------------------------------ korumalı dallar

    [Theory]
    [InlineData("SYSTEM")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\Tcpip")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules")]
    [InlineData("SECURITY")]
    [InlineData("SAM")]
    [InlineData(@"HARDWARE\DEVICEMAP")]
    public void SistemKovaniHicbirKosuldaSilinemez(string keyPath)
    {
        RegistryVerdict verdict = _guard.Inspect(Value(RegistryHive.LocalMachine, keyPath));

        Assert.False(verdict.IsAllowed);
        Assert.Equal(RegistryDenialReason.ProtectedSystemKey, verdict.Reason);
    }

    [Theory]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\Packages")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SideBySide\Winners")]
    [InlineData(@"SOFTWARE\Microsoft\.NETFramework\v4.0.30319")]
    [InlineData(@"SOFTWARE\Microsoft\Windows Defender\Exclusions")]
    [InlineData(@"SOFTWARE\Microsoft\Cryptography\RNG")]
    [InlineData(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon")]
    public void BilesenVeGuvenlikDallariSilinemez(string keyPath) =>
        Assert.False(_guard.Inspect(Value(RegistryHive.LocalMachine, keyPath)).IsAllowed, keyPath);

    [Theory]
    [InlineData(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate")]
    [InlineData(RegistryHive.CurrentUser, @"SOFTWARE\Policies\Microsoft\Windows\Explorer")]
    public void IlkelerSilinemez(RegistryHive hive, string keyPath)
    {
        RegistryVerdict verdict = _guard.Inspect(Value(hive, keyPath));

        Assert.Equal(RegistryDenialReason.ProtectedPolicy, verdict.Reason);
    }

    // ------------------------------------------------------------------ kapsam dışı

    [Theory]
    [InlineData(@"SOFTWARE\Microsoft\Office")]
    [InlineData(@"SOFTWARE\Adobe")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion")]
    public void KapsamDisindakiDallaraDokunulmaz(string keyPath)
    {
        RegistryVerdict verdict = _guard.Inspect(Value(RegistryHive.LocalMachine, keyPath));

        Assert.Equal(RegistryDenialReason.OutsideAllowedScope, verdict.Reason);
    }

    [Fact]
    public void OnEkiAyniOlanKomsuDalKapsamDisidir()
    {
        // "SOFTWARE\ClassesYedek" yolu "SOFTWARE\Classes" kapsamının altında değildir.
        RegistryVerdict verdict = _guard.Inspect(Value(RegistryHive.LocalMachine, @"SOFTWARE\ClassesYedek\Bir"));

        Assert.Equal(RegistryDenialReason.OutsideAllowedScope, verdict.Reason);
    }

    // ------------------------------------------------------------------ köke yakınlık

    [Fact]
    public void IzinliKapsaminKendisiSilinemez()
    {
        // İçi temizlenir ama dalın kendisi ayakta kalmalı.
        RegistryVerdict verdict = _guard.Inspect(
            Key(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs"));

        Assert.Equal(RegistryDenialReason.TooCloseToRoot, verdict.Reason);
    }

    [Fact]
    public void SigYolAnahtarOlarakSilinemez() =>
        Assert.Equal(
            RegistryDenialReason.OutsideAllowedScope,
            _guard.Inspect(Key(RegistryHive.LocalMachine, "SOFTWARE")).Reason);

    // ------------------------------------------------------------------ geçersiz girdi

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\\")]
    public void BosYolReddedilir(string keyPath) =>
        Assert.Equal(
            RegistryDenialReason.InvalidLocation,
            _guard.Inspect(Value(RegistryHive.LocalMachine, keyPath)).Reason);

    [Theory]
    [InlineData(RegistryHive.ClassesRoot)]
    [InlineData(RegistryHive.Users)]
    [InlineData(RegistryHive.CurrentConfig)]
    public void DesteklenmeyenKovanlarReddedilir(RegistryHive hive)
    {
        // HKCR birleştirilmiş görünüm: silme hangi kovana yazıldığını belirsiz bırakır.
        RegistryVerdict verdict = _guard.Inspect(Value(hive, @"SOFTWARE\Classes\.txt"));

        Assert.Equal(RegistryDenialReason.InvalidLocation, verdict.Reason);
    }

    [Fact]
    public void GuvenlikDuvariKurallariKapsamDisi()
    {
        // Bu tarayıcı v1'e bilerek alınmadı: kuralları HKLM\SYSTEM altında duruyor
        // ve en tehlikeli kovana istisna açmak güvenlik katmanını anlamsızlaştırır.
        Assert.False(_guard.Inspect(Value(
            RegistryHive.LocalMachine,
            @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules")).IsAllowed);
    }
}
