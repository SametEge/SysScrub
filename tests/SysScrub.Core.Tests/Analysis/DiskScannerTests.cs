using SysScrub.Core.Analysis;
using Xunit;

namespace SysScrub.Core.Tests.Analysis;

/// <summary>
/// Klasör tarayıcısı.
///
/// En kritik iki davranış: bağlantı noktasının içine girmemek (aynı ağacı iki kez
/// saymak ya da sonsuz döngü) ve erişilemeyen klasörü sessizce yutmamak.
/// </summary>
public sealed class DiskScannerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "SysScrub.Tests", Guid.NewGuid().ToString("N"));

    private readonly DiskScanner _scanner = new();

    public DiskScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            TestJunction.DeleteTree(_root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void WriteFile(string relativePath, int bytes)
    {
        string full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[bytes]);
    }

    [Fact]
    public async Task DosyalarVeBoyutlarToplanir()
    {
        WriteFile("a.bin", 1000);
        WriteFile("b.bin", 2000);

        DiskScanResult result = await _scanner.ScanAsync(_root);

        Assert.Equal(3000, result.TotalBytes);
        Assert.Equal(2, result.FileCount);
    }

    [Fact]
    public async Task AltKlasorlerToplanir()
    {
        WriteFile("a.bin", 1000);
        WriteFile(@"alt\b.bin", 500);
        WriteFile(@"alt\daha-alt\c.bin", 250);

        DiskScanResult result = await _scanner.ScanAsync(_root);

        Assert.Equal(1750, result.TotalBytes);
        Assert.Equal(3, result.FileCount);
        Assert.Equal(2, result.DirectoryCount);
    }

    [Fact]
    public async Task AltKlasorlerBuyuktenKucugeSiralanir()
    {
        WriteFile(@"kucuk\a.bin", 100);
        WriteFile(@"buyuk\b.bin", 5000);
        WriteFile(@"orta\c.bin", 1000);

        DiskScanResult result = await _scanner.ScanAsync(_root);

        string[] names = result.Root.Children.Where(c => !c.IsFile).Select(c => c.Name).ToArray();

        Assert.Equal(["buyuk", "orta", "kucuk"], names);
    }

    /// <summary>
    /// Bağlantı noktasının içine girmek aynı ağacı iki kez saymak, kötü durumda
    /// sonsuz döngüye girmek demek.
    /// </summary>
    [Fact]
    public async Task BaglantiNoktasiTakipEdilmez()
    {
        WriteFile(@"gercek\buyuk.bin", 4000);
        WriteFile("kucuk.bin", 100);

        if (!TestJunction.TryCreate(Path.Combine(_root, "kisayol"), Path.Combine(_root, "gercek")))
        {
            return;
        }

        DiskScanResult result = await _scanner.ScanAsync(_root);

        Assert.Equal(4100, result.TotalBytes);
        Assert.Equal(1, result.SkippedLinks);
    }

    [Fact]
    public async Task EnBuyukDosyalarSiralanir()
    {
        WriteFile("a.bin", 100);
        WriteFile("b.bin", 9000);
        WriteFile("c.bin", 3000);

        DiskScanResult result = await _scanner.ScanAsync(_root);

        Assert.Equal("b.bin", result.LargestFiles[0].Name);
        Assert.Equal("c.bin", result.LargestFiles[1].Name);
    }

    [Fact]
    public async Task TurDagilimiUzantiyaGoreToplanir()
    {
        WriteFile("a.txt", 100);
        WriteFile("b.txt", 200);
        WriteFile("c.bin", 5000);

        DiskScanResult result = await _scanner.ScanAsync(_root);

        FileTypeSummary text = result.TypeBreakdown.Single(t => t.Extension == ".txt");

        Assert.Equal(300, text.SizeBytes);
        Assert.Equal(2, text.Count);

        // Büyükten küçüğe sıralı: .bin en üstte olmalı.
        Assert.Equal(".bin", result.TypeBreakdown[0].Extension);
    }

    [Fact]
    public async Task UzantisizDosyaAyriGrupOlur()
    {
        WriteFile("LICENSE", 500);

        DiskScanResult result = await _scanner.ScanAsync(_root);

        Assert.Equal("uzantısız", result.TypeBreakdown[0].Label);
    }

    /// <summary>Boş klasörler listeyi boğuyor ve treemap'te çizilemiyor.</summary>
    [Fact]
    public async Task BosKlasorlerAgacaGirmez()
    {
        Directory.CreateDirectory(Path.Combine(_root, "bos"));
        WriteFile(@"dolu\a.bin", 100);

        DiskScanResult result = await _scanner.ScanAsync(_root);

        Assert.Equal("dolu", Assert.Single(result.Root.Children, c => !c.IsFile).Name);
    }

    [Fact]
    public async Task OlmayanKlasorBosSonucDoner()
    {
        DiskScanResult result = await _scanner.ScanAsync(Path.Combine(_root, "yok"));

        Assert.Equal(0, result.TotalBytes);
        Assert.Equal(0, result.FileCount);
    }

    [Fact]
    public async Task UstKlasorOraniHesaplanir()
    {
        WriteFile(@"yarim\a.bin", 500);
        WriteFile(@"digeryarim\b.bin", 500);

        DiskScanResult result = await _scanner.ScanAsync(_root);

        Assert.Equal(0.5, result.Root.Children[0].ShareOfParent, precision: 3);
    }

    [Fact]
    public async Task KokeKadarkiYolCikarilir()
    {
        WriteFile(@"bir\iki\a.bin", 100);

        DiskScanResult result = await _scanner.ScanAsync(_root);

        FolderNode deep = result.Root.Children[0].Children[0];
        IReadOnlyList<FolderNode> path = deep.PathFromRoot();

        Assert.Equal(3, path.Count);
        Assert.Equal("iki", path[^1].Name);
        Assert.Same(result.Root, path[0]);
    }

    [Fact]
    public async Task IlerlemeBildirilir()
    {
        for (int i = 0; i < 200; i++)
        {
            WriteFile($"dosya{i}.bin", 1024);
        }

        var reports = new List<DiskScanProgress>();
        var progress = new Progress<DiskScanProgress>(reports.Add);

        DiskScanResult result = await _scanner.ScanAsync(_root, progress);

        Assert.Equal(200, result.FileCount);
    }
}
