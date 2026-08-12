using SysScrub.Core.Machine;
using SysScrub.Core.Rules;
using SysScrub.Core.Safety;
using Xunit;

namespace SysScrub.Core.Tests.Safety;

/// <summary>
/// Bu testler projedeki en önemli testler: SafetyGuard'daki bir gerileme,
/// kullanıcının verisini silen bir uygulama demek.
/// </summary>
public sealed class SafetyGuardTests : IDisposable
{
    private readonly string _sandbox;
    private readonly string _allowedRoot;
    private readonly SafetyGuard _guard = new(new PathResolver());

    public SafetyGuardTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "SysScrub.Tests", Guid.NewGuid().ToString("N"));
        _allowedRoot = Path.Combine(_sandbox, "allowed");

        Directory.CreateDirectory(_allowedRoot);
        Directory.CreateDirectory(Path.Combine(_sandbox, "yasak"));
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

    // ------------------------------------------------------------------ izin verilen durum

    [Fact]
    public void IzinliKokAltindakiDosyayaIzinVerilir()
    {
        string file = CreateFile(_allowedRoot, "onbellek.tmp");

        Assert.True(_guard.InspectFile(file, _allowedRoot).IsAllowed);
    }

    [Fact]
    public void IzinliKokAltindakiAltKlasoreIzinVerilir()
    {
        string directory = Path.Combine(_allowedRoot, "alt");
        Directory.CreateDirectory(directory);

        Assert.True(_guard.InspectDirectory(directory, _allowedRoot).IsAllowed);
    }

    // ------------------------------------------------------------------ kaçış denemeleri

    [Fact]
    public void UstKlasoreCikanYolReddedilir()
    {
        // Kural dosyası bozulmuş ya da kötü niyetli olsa bile bu geçmemeli.
        string escape = Path.Combine(_allowedRoot, "..", "yasak", "veri.txt");

        GuardVerdict verdict = _guard.InspectFile(escape, _allowedRoot);

        Assert.False(verdict.IsAllowed);
        Assert.Equal(GuardDenialReason.OutsideAllowedRoot, verdict.Reason);
    }

    [Fact]
    public void IzinliKokDisindakiYolReddedilir()
    {
        string outside = CreateFile(Path.Combine(_sandbox, "yasak"), "veri.txt");

        Assert.Equal(GuardDenialReason.OutsideAllowedRoot, _guard.InspectFile(outside, _allowedRoot).Reason);
    }

    [Fact]
    public void BenzerAdliKardesKlasorKokAltiSayilmaz()
    {
        // "C:\...\allowed-yedek" yolu "C:\...\allowed" kökünün altında değildir.
        string sibling = Path.Combine(_sandbox, "allowed-yedek");
        Directory.CreateDirectory(sibling);
        string file = CreateFile(sibling, "veri.txt");

        Assert.Equal(GuardDenialReason.OutsideAllowedRoot, _guard.InspectFile(file, _allowedRoot).Reason);
    }

    [Fact]
    public void KokunKendisiSilinemez() =>
        Assert.Equal(GuardDenialReason.OutsideAllowedRoot, _guard.InspectDirectory(_allowedRoot, _allowedRoot).Reason);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("goreli\\yol.txt")]
    public void GecersizYolReddedilir(string path) =>
        Assert.False(_guard.InspectFile(path, _allowedRoot).IsAllowed);

    [Theory]
    [InlineData(@"\\sunucu\paylasim\veri.txt")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\?\C:\Windows\System32\kernel32.dll")]
    public void AgVeAygitYollariReddedilir(string path) =>
        Assert.Equal(GuardDenialReason.NonLocalPath, _guard.InspectFile(path, _allowedRoot).Reason);

    // ------------------------------------------------------------------ korumalı ağaçlar

    [Theory]
    [InlineData("System32", "kernel32.dll")]
    [InlineData("WinSxS", "manifest.xml")]
    [InlineData("Fonts", "segoeui.ttf")]
    [InlineData("servicing", "sil.dat")]
    public void WindowsBilesenleriReddedilir(string folder, string file)
    {
        string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        string path = Path.Combine(systemRoot, folder, file);

        // Kural kökü olarak Windows klasörü verilse bile geçmemeli.
        GuardVerdict verdict = _guard.InspectFile(path, systemRoot);

        Assert.Equal(GuardDenialReason.ProtectedSystemDirectory, verdict.Reason);
    }

    [Fact]
    public void ProgramFilesReddedilir()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string path = Path.Combine(programFiles, "BirUygulama", "veri.dat");

        Assert.Equal(GuardDenialReason.ProtectedSystemDirectory, _guard.InspectFile(path, programFiles).Reason);
    }

    [Theory]
    [InlineData(Environment.SpecialFolder.MyDocuments)]
    [InlineData(Environment.SpecialFolder.DesktopDirectory)]
    [InlineData(Environment.SpecialFolder.MyPictures)]
    public void KullaniciIcerigiReddedilir(Environment.SpecialFolder folder)
    {
        string root = Environment.GetFolderPath(folder);

        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        string path = Path.Combine(root, "onemli-dosya.docx");

        Assert.Equal(GuardDenialReason.UserContent, _guard.InspectFile(path, root).Reason);
    }

    [Fact]
    public void UygulamaninKendiVerisiReddedilir()
    {
        string path = Path.Combine(AppPaths.DataDirectory, "logs", "sysscrub.log");

        Assert.Equal(GuardDenialReason.ApplicationOwnData, _guard.InspectFile(path, AppPaths.DataDirectory).Reason);
    }

    [Fact]
    public void SurucuKokuSilinemez()
    {
        string root = Path.GetPathRoot(Environment.SystemDirectory)!;

        Assert.False(_guard.InspectDirectory(root, root).IsAllowed);
    }

    // ------------------------------------------------------------------ öznitelik denetimi

    [Fact]
    public void BaglantiNoktasiReddedilir()
    {
        GuardVerdict verdict = SafetyGuard.InspectAttributes(FileAttributes.Directory | FileAttributes.ReparsePoint);

        Assert.Equal(GuardDenialReason.ReparsePoint, verdict.Reason);
    }

    [Theory]
    [InlineData(0x00400000)] // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS — OneDrive "yalnızca çevrimiçi"
    [InlineData(0x00040000)] // FILE_ATTRIBUTE_RECALL_ON_OPEN
    [InlineData(0x00001000)] // FILE_ATTRIBUTE_OFFLINE
    public void BulutYerTutucusuReddedilir(int rawAttribute)
    {
        var attributes = FileAttributes.Normal | (FileAttributes)rawAttribute;

        Assert.Equal(GuardDenialReason.CloudPlaceholder, SafetyGuard.InspectAttributes(attributes).Reason);
    }

    [Fact]
    public void SiradanDosyaOzniteligiGecer() =>
        Assert.True(SafetyGuard.InspectAttributes(FileAttributes.Normal | FileAttributes.Archive).IsAllowed);

    [Fact]
    public void GercekBaglantiNoktasiIzlenmez()
    {
        string target = Path.Combine(_sandbox, "hedef");
        string junction = Path.Combine(_allowedRoot, "baglanti");
        Directory.CreateDirectory(target);

        if (!TestJunction.TryCreate(junction, target))
        {
            // Bağlantı oluşturulamadıysa (politika/izin) test bir şey iddia edemez.
            return;
        }

        Assert.Equal(GuardDenialReason.ReparsePoint, _guard.InspectDirectory(junction, _allowedRoot).Reason);
        Assert.False(_guard.CanTraverse(junction));
    }

    // ------------------------------------------------------------------ yardımcılar

    private static string CreateFile(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, "test");
        return path;
    }
}
