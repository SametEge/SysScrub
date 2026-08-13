using SysScrub.Core.Programs;
using Xunit;

namespace SysScrub.Core.Tests.Programs;

/// <summary>Klasör boyutu ölçümü.</summary>
public sealed class ProgramSizeCalculatorTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "SysScrub.Tests", Guid.NewGuid().ToString("N"));

    private readonly ProgramSizeCalculator _calculator = new();

    public ProgramSizeCalculatorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            // Özyinelemeli silme bağlantı noktalarına takılıyor; önce onlar kaldırılıyor.
            TestJunction.DeleteTree(_root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string WriteFile(string relativePath, int bytes)
    {
        string full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[bytes]);

        return full;
    }

    [Fact]
    public void KlasordekiDosyalarToplanir()
    {
        WriteFile("a.bin", 1000);
        WriteFile("b.bin", 2000);

        Assert.Equal(3000, _calculator.Measure(_root));
    }

    [Fact]
    public void AltKlasorlerDeSayilir()
    {
        WriteFile("a.bin", 1000);
        WriteFile(@"alt\b.bin", 500);
        WriteFile(@"alt\daha-alt\c.bin", 250);

        Assert.Equal(1750, _calculator.Measure(_root));
    }

    [Fact]
    public void BosKlasorSifirDoner() => Assert.Equal(0, _calculator.Measure(_root));

    [Fact]
    public void OlmayanKlasorSifirDoner() =>
        Assert.Equal(0, _calculator.Measure(Path.Combine(_root, "yok")));

    /// <summary>
    /// Bağlantı noktasının içine girmek aynı ağacı iki kez saymak ya da bambaşka
    /// bir yeri bu programa yazmak demek.
    /// </summary>
    [Fact]
    public void BaglantiNoktasiTakipEdilmez()
    {
        WriteFile(@"gercek\buyuk.bin", 4000);
        WriteFile("kucuk.bin", 100);

        string junction = Path.Combine(_root, "kisayol");

        if (!TestJunction.TryCreate(junction, Path.Combine(_root, "gercek")))
        {
            // Bağlantı oluşturulamıyorsa (yetki yok) test anlamsız; atlanıyor.
            return;
        }

        // 4000 + 100 bekleniyor: bağlantı üzerinden ikinci kez sayılmamalı.
        Assert.Equal(4100, _calculator.Measure(_root));
    }

    [Fact]
    public async Task OlcumHerSonucuAyriBildirir()
    {
        WriteFile("a.bin", 1000);

        var results = new List<ProgramSize>();
        var progress = new Progress<ProgramSize>(results.Add);

        InstalledProgram program = new()
        {
            Id = "test",
            Name = "Test",
            Source = ProgramSource.Registry,
            InstallLocation = _root
        };

        await _calculator.MeasureAsync([program], progress);

        // Progress geri çağrıları eşzamansız; kısa bir bekleme gerekiyor.
        for (int i = 0; i < 20 && results.Count == 0; i++)
        {
            await Task.Delay(25);
        }

        ProgramSize size = Assert.Single(results);
        Assert.Equal("test", size.ProgramId);
        Assert.Equal(1000, size.Bytes);
    }

    [Fact]
    public async Task KlasoruOlmayanProgramOlculmez()
    {
        var results = new List<ProgramSize>();

        InstalledProgram program = new()
        {
            Id = "test",
            Name = "Test",
            Source = ProgramSource.Registry,
            InstallLocation = Path.Combine(_root, "hic-yok")
        };

        await _calculator.MeasureAsync([program], new Progress<ProgramSize>(results.Add));

        Assert.Empty(results);
    }
}
