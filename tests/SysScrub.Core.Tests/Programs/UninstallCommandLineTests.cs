using SysScrub.Core.Programs;
using Xunit;

namespace SysScrub.Core.Tests.Programs;

/// <summary>
/// Kaldırma komutu ayrıştırıcısı.
///
/// Bu sınıfta bir hata, kaldırıcıyı hiç çalıştıramamak demek: yol yanlış bölünürse
/// "dosya bulunamadı" alırız ve kullanıcı programı kaldıramaz. Örneklerin hepsi
/// gerçek registry kayıtlarından alındı.
/// </summary>
public sealed class UninstallCommandLineTests
{
    [Fact]
    public void TirnakliYolArgumansizAyrisir()
    {
        UninstallCommand command = UninstallCommandLine.Parse(@"""C:\Program Files\Git\unins000.exe""");

        Assert.Equal(@"C:\Program Files\Git\unins000.exe", command.FileName);
        Assert.Equal(string.Empty, command.Arguments);
    }

    [Fact]
    public void TirnakliYolArgumanlaAyrisir()
    {
        UninstallCommand command = UninstallCommandLine.Parse(
            @"""C:\Program Files\Git\unins000.exe"" /SILENT");

        Assert.Equal(@"C:\Program Files\Git\unins000.exe", command.FileName);
        Assert.Equal("/SILENT", command.Arguments);
    }

    /// <summary>
    /// Tırnaksız ve boşluklu yolda dosya hiç bulunamazsa ilk boşluğa kadarı komut
    /// sayılıyor. Yol gerçekten varsa doğru bölünüyor — bir sonraki test onu ölçüyor.
    /// </summary>
    [Fact]
    public void HicBulunamayanTirnaksizYolIlkBoslugaKadarAyrisir()
    {
        UninstallCommand command = UninstallCommandLine.Parse(
            $@"C:\Hic Olmayan {Guid.NewGuid():N}\Bir Yer\kaldir.exe");

        Assert.Equal(@"C:\Hic", command.FileName);
    }

    [Fact]
    public void VarOlanTirnaksizBoslukluYolTamOlarakBulunur()
    {
        // Gerçek bir dosya kuruyoruz: ayrıştırıcı "var olan en uzun önek" kuralını
        // ancak dosya sisteminde karşılığı olduğunda uygulayabiliyor.
        string directory = Path.Combine(Path.GetTempPath(), $"sysscrub testleri {Guid.NewGuid():N}");
        string executable = Path.Combine(directory, "kaldirici arac.exe");

        Directory.CreateDirectory(directory);
        File.WriteAllText(executable, string.Empty);

        try
        {
            UninstallCommand command = UninstallCommandLine.Parse($"{executable} /U {{GUID}}");

            Assert.Equal(executable, command.FileName);
            Assert.Equal("/U {GUID}", command.Arguments);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MsiKomutuAyrisir()
    {
        UninstallCommand command = UninstallCommandLine.Parse(
            "MsiExec.exe /I{90160000-008C-0000-1000-0000000FF1CE}");

        Assert.Equal("MsiExec.exe", command.FileName);
        Assert.Equal("/I{90160000-008C-0000-1000-0000000FF1CE}", command.Arguments);
    }

    [Fact]
    public void OrtamDegiskeniGenisletilir()
    {
        UninstallCommand command = UninstallCommandLine.Parse(@"%SystemRoot%\system32\kaldir.exe /q");

        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            command.FileName,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('%', command.FileName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BosKomutGecersizdir(string? command) =>
        Assert.False(UninstallCommandLine.Parse(command).IsValid);

    // ------------------------------------------------------------------ sessiz MSI

    [Fact]
    public void MsiSessizKaldirmayaCevrilir()
    {
        UninstallCommand? silent = UninstallCommandLine.ToSilentMsi(
            "MsiExec.exe /I{90160000-008C-0000-1000-0000000FF1CE}");

        Assert.NotNull(silent);
        Assert.Equal("msiexec.exe", silent!.Value.FileName);
        Assert.Contains("/x {90160000-008C-0000-1000-0000000FF1CE}", silent.Value.Arguments);
        Assert.Contains("/qn", silent.Value.Arguments);
    }

    /// <summary>
    /// /norestart olmazsa sessiz MSI kaldırması bilgisayarı sormadan yeniden
    /// başlatabiliyor; kullanıcının açık dosyaları gider.
    /// </summary>
    [Fact]
    public void SessizMsiYenidenBaslatmayiEngeller()
    {
        UninstallCommand? silent = UninstallCommandLine.ToSilentMsi(
            "MsiExec.exe /X{90160000-008C-0000-1000-0000000FF1CE}");

        Assert.Contains("/norestart", silent!.Value.Arguments);
    }

    [Fact]
    public void MsiOlmayanKomutSessizeCevrilmez() =>
        Assert.Null(UninstallCommandLine.ToSilentMsi(@"""C:\Program Files\Git\unins000.exe"""));

    /// <summary>Ürün kodu okunamazsa tahmin etmiyoruz; normal kaldırıcı çalışır.</summary>
    [Fact]
    public void UrunKoduOlmayanMsiSessizeCevrilmez() =>
        Assert.Null(UninstallCommandLine.ToSilentMsi("MsiExec.exe /X"));

    [Theory]
    [InlineData("MsiExec.exe /X{GUID}", true)]
    [InlineData("msiexec /x {GUID}", true)]
    [InlineData(@"C:\Program Files\App\unins.exe", false)]
    public void MsiTespitiBuyukKucukHarfeDuyarsiz(string command, bool expected) =>
        Assert.Equal(expected, UninstallCommandLine.IsMsi(command));
}
