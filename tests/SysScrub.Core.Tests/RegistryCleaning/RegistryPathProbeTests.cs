using SysScrub.Core.RegistryCleaning;
using Xunit;

namespace SysScrub.Core.Tests.RegistryCleaning;

/// <summary>
/// Bu testlerin çoğu "Unknown dönmeli" diyor. Sebebi şu: bir registry temizleyicisinin
/// yapabileceği en kötü şey, çözemediği bir yolu "dosya yok" sayıp çalışan kaydı silmek.
/// Emin olamadığımız her durumda bulgu üretmiyoruz.
/// </summary>
public sealed class RegistryPathProbeTests : IDisposable
{
    private readonly string _sandbox;
    private readonly string _existingFile;

    public RegistryPathProbeTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "SysScrub.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);

        _existingFile = Path.Combine(_sandbox, "uygulama.exe");
        File.WriteAllBytes(_existingFile, new byte[16]);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_sandbox, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ------------------------------------------------------------------ var olan dosyalar

    [Fact]
    public void DuzYolBulunur() =>
        Assert.Equal(RegistryPathProbe.ProbeResult.Exists, RegistryPathProbe.Probe(_existingFile, out _));

    [Fact]
    public void TirnakliYolBulunur() =>
        Assert.Equal(RegistryPathProbe.ProbeResult.Exists, RegistryPathProbe.Probe($"\"{_existingFile}\"", out _));

    [Fact]
    public void TirnakliYolArgumanlaBulunur() =>
        Assert.Equal(
            RegistryPathProbe.ProbeResult.Exists,
            RegistryPathProbe.Probe($"\"{_existingFile}\" --arka-planda /sessiz", out _));

    [Fact]
    public void TirnaksizYolEgikCizgiliArgumanlaBulunur() =>
        Assert.Equal(
            RegistryPathProbe.ProbeResult.Exists,
            RegistryPathProbe.Probe($"{_existingFile} /baslangic", out _));

    [Fact]
    public void BosluklarIceenTirnaksizYolBulunur()
    {
        // "C:\...\iki kelime\uygulama.exe argüman" — hangi parçanın yol olduğu belirsiz,
        // çözümleyici var olan en uzun öneki bulmalı.
        string directory = Path.Combine(_sandbox, "iki kelime");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "uygulama.exe");
        File.WriteAllBytes(file, new byte[8]);

        Assert.Equal(RegistryPathProbe.ProbeResult.Exists, RegistryPathProbe.Probe($"{file} argüman", out _));
    }

    [Fact]
    public void OrtamDegiskeniGenisletilir() =>
        Assert.Equal(
            RegistryPathProbe.ProbeResult.Exists,
            RegistryPathProbe.Probe(@"%SystemRoot%\explorer.exe", out _));

    [Fact]
    public void KaynakIndeksiYoldanAyiklanir()
    {
        RegistryPathProbe.ProbeResult result =
            RegistryPathProbe.Probe(@"%SystemRoot%\system32\shell32.dll,-123", out string resolved);

        Assert.Equal(RegistryPathProbe.ProbeResult.Exists, result);
        Assert.EndsWith("shell32.dll", resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DolayliDizeOnekiAyiklanir() =>
        Assert.Equal(
            RegistryPathProbe.ProbeResult.Exists,
            RegistryPathProbe.Probe(@"@%SystemRoot%\system32\shell32.dll,-21787", out _));

    [Fact]
    public void SysWow64IkiziDenenir()
    {
        // 32-bit kayıtlar System32 yazar, dosya SysWOW64'tedir. 64-bit süreçten
        // yönlendirme olmadığı için ikizi elle denenmezse kayıt ölü sanılır.
        string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        string wowOnly = Path.Combine(systemRoot, "SysWOW64", "wow64.dll");

        if (!File.Exists(wowOnly))
        {
            return;
        }

        Assert.Equal(
            RegistryPathProbe.ProbeResult.Exists,
            RegistryPathProbe.Probe(Path.Combine(systemRoot, "System32", "wow64.dll"), out _));
    }

    // ------------------------------------------------------------------ gerçekten eksik

    [Fact]
    public void OlmayanDosyaEksikSayilir() =>
        Assert.Equal(
            RegistryPathProbe.ProbeResult.Missing,
            RegistryPathProbe.Probe(Path.Combine(_sandbox, "yok-boyle-bir-dosya.exe"), out _));

    [Fact]
    public void OlmayanTirnakliDosyaEksikSayilir() =>
        Assert.Equal(
            RegistryPathProbe.ProbeResult.Missing,
            RegistryPathProbe.Probe($"\"{Path.Combine(_sandbox, "yok.exe")}\" /flag", out _));

    // ------------------------------------------------------------------ yol olmayanlar

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{21EC2020-3AEA-1069-A2DD-08002B30309D}")]
    [InlineData("https://ornek.com/sayfa")]
    [InlineData("mailto:biri@ornek.com")]
    [InlineData("shell:AppsFolder")]
    [InlineData("res://ieframe.dll/hata.htm")]
    [InlineData("Uygulama.Belge.1")]
    [InlineData("notepad.exe")]
    public void YolOlmayanDegerlerBulguUretmez(string value) =>
        Assert.Equal(RegistryPathProbe.ProbeResult.Unknown, RegistryPathProbe.Probe(value, out _));

    [Fact]
    public void CozulemeyenDegiskenBulguUretmez()
    {
        // Bilinmeyen bir değişken genişletilemez; "yok" demek yerine bilmiyoruz demeliyiz.
        Assert.Equal(
            RegistryPathProbe.ProbeResult.Unknown,
            RegistryPathProbe.Probe(@"%BOYLE_BIR_DEGISKEN_YOK%\uygulama.exe", out _));
    }

    [Fact]
    public void NullVeBosDegerBulguUretmez()
    {
        Assert.Equal(RegistryPathProbe.ProbeResult.Unknown, RegistryPathProbe.Probe(null, out _));
        Assert.Equal(RegistryPathProbe.ProbeResult.Unknown, RegistryPathProbe.Probe(string.Empty, out _));
    }

    [Fact]
    public void KlasorYoluDaGecerliSayilir() =>
        Assert.Equal(RegistryPathProbe.ProbeResult.Exists, RegistryPathProbe.Probe(_sandbox, out _));
}
