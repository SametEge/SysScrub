using System.Text;
using SysScrub.Core.Analysis;
using Xunit;

namespace SysScrub.Core.Tests.Analysis;

/// <summary>
/// Yinelenen dosya bulucu.
///
/// En kritik güvence: aynı boyutta ama farklı içerikli dosyalar yinelenen
/// SAYILMAMALI. Yanlış pozitif burada, kullanıcının benzersiz bir dosyayı
/// silmesi demek.
/// </summary>
public sealed class DuplicateFinderTests : IDisposable
{
    /// <summary>Bulucu 1 MB altını görmezden geliyor; testler bunun üstüne çıkmak zorunda.</summary>
    private const int LargeEnough = 2 * 1024 * 1024;

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "SysScrub.Tests", Guid.NewGuid().ToString("N"));

    private readonly DiskScanner _scanner = new();
    private readonly DuplicateFinder _finder = new();

    public DuplicateFinderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Belirli bir tohumdan üretilmiş, sıkıştırılamayan içerik yazar.</summary>
    private void WriteFile(string relativePath, int seed, int bytes = LargeEnough)
    {
        string full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var random = new Random(seed);
        var buffer = new byte[bytes];
        random.NextBytes(buffer);

        File.WriteAllBytes(full, buffer);
    }

    private async Task<DuplicateScanResult> FindAsync()
    {
        DiskScanResult scan = await _scanner.ScanAsync(_root);

        return await _finder.FindAsync(scan.Root);
    }

    [Fact]
    public async Task AyniIcerikliDosyalarGruplanir()
    {
        WriteFile("a.bin", seed: 1);
        WriteFile(@"alt\b.bin", seed: 1);

        DuplicateScanResult result = await FindAsync();

        DuplicateGroup group = Assert.Single(result.Groups);
        Assert.Equal(2, group.Paths.Count);
    }

    /// <summary>
    /// Aynı boyut yinelenen olmak için yeterli değil. Bu testin düşmesi,
    /// kullanıcının benzersiz bir dosyayı silmesi anlamına gelir.
    /// </summary>
    [Fact]
    public async Task AyniBoyutFarkliIcerikYinelenenSayilmaz()
    {
        WriteFile("a.bin", seed: 1);
        WriteFile("b.bin", seed: 2);

        DuplicateScanResult result = await FindAsync();

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task FarkliBoyutlarKarsilastirilmaz()
    {
        WriteFile("a.bin", seed: 1, bytes: LargeEnough);
        WriteFile("b.bin", seed: 1, bytes: LargeEnough + 4096);

        DuplicateScanResult result = await FindAsync();

        Assert.Empty(result.Groups);
        // Tek başına kalan boyut ilk aşamada eleniyor; hiç okuma yapılmıyor.
        Assert.Equal(0, result.FilesHashed);
    }

    [Fact]
    public async Task UcKopyaTekGruptaToplanir()
    {
        WriteFile("a.bin", seed: 7);
        WriteFile(@"bir\b.bin", seed: 7);
        WriteFile(@"iki\c.bin", seed: 7);

        DuplicateGroup group = Assert.Single((await FindAsync()).Groups);

        Assert.Equal(3, group.Paths.Count);
    }

    /// <summary>Kazanç bir kopya korunarak hesaplanıyor; hepsini silmek önerilmiyor.</summary>
    [Fact]
    public async Task KazancBirKopyaKorunarakHesaplanir()
    {
        WriteFile("a.bin", seed: 3);
        WriteFile("b.bin", seed: 3);
        WriteFile("c.bin", seed: 3);

        DuplicateGroup group = Assert.Single((await FindAsync()).Groups);

        Assert.Equal(LargeEnough * 2L, group.RecoverableBytes);
        Assert.Equal(LargeEnough, group.SizeBytes);
    }

    [Fact]
    public async Task KucukDosyalarGormezdenGelinir()
    {
        WriteFile("a.bin", seed: 5, bytes: 1024);
        WriteFile("b.bin", seed: 5, bytes: 1024);

        Assert.Empty((await FindAsync()).Groups);
    }

    [Fact]
    public async Task YinelenenYokkenSonucBos()
    {
        WriteFile("a.bin", seed: 1);
        WriteFile("b.bin", seed: 2, bytes: LargeEnough + 1024);

        DuplicateScanResult result = await FindAsync();

        Assert.Empty(result.Groups);
        Assert.Equal(0, result.DuplicateCount);
        Assert.Equal(0, result.RecoverableBytes);
    }

    [Fact]
    public async Task BirdenCokGrupKazancaGoreSiralanir()
    {
        // Küçük grup: iki kopya. Büyük grup: üç kopya, daha çok kazanç.
        WriteFile("kucuk1.bin", seed: 11, bytes: LargeEnough);
        WriteFile("kucuk2.bin", seed: 11, bytes: LargeEnough);

        WriteFile("buyuk1.bin", seed: 12, bytes: LargeEnough + 8192);
        WriteFile("buyuk2.bin", seed: 12, bytes: LargeEnough + 8192);
        WriteFile("buyuk3.bin", seed: 12, bytes: LargeEnough + 8192);

        DuplicateScanResult result = await FindAsync();

        Assert.Equal(2, result.Groups.Count);
        Assert.True(result.Groups[0].RecoverableBytes > result.Groups[1].RecoverableBytes);
    }

    [Fact]
    public async Task ToplamSayilarDogru()
    {
        WriteFile("a.bin", seed: 21);
        WriteFile("b.bin", seed: 21);
        WriteFile("c.bin", seed: 21);

        DuplicateScanResult result = await FindAsync();

        Assert.Equal(2, result.DuplicateCount);
        Assert.Equal(LargeEnough * 2L, result.RecoverableBytes);
    }

    /// <summary>
    /// Aynı başlıkla başlayan farklı dosyalar yalnızca baştan ayrılamıyor;
    /// bulucu son parçayı da örneklediği için ayrılmaları gerekiyor.
    /// </summary>
    [Fact]
    public async Task AyniBaslangicliFarkliDosyalarAyrilir()
    {
        var shared = new byte[8192];
        new Random(99).NextBytes(shared);

        WriteSharedPrefix("a.bin", shared, tailSeed: 1);
        WriteSharedPrefix("b.bin", shared, tailSeed: 2);

        Assert.Empty((await FindAsync()).Groups);
    }

    private void WriteSharedPrefix(string name, byte[] prefix, int tailSeed)
    {
        var tail = new byte[LargeEnough - prefix.Length];
        new Random(tailSeed).NextBytes(tail);

        using FileStream stream = File.Create(Path.Combine(_root, name));
        stream.Write(prefix);
        stream.Write(tail);
    }

    [Fact]
    public async Task MetinDosyalariDaKarsilastirilir()
    {
        string content = new('x', LargeEnough);

        File.WriteAllText(Path.Combine(_root, "a.txt"), content, Encoding.ASCII);
        File.WriteAllText(Path.Combine(_root, "b.txt"), content, Encoding.ASCII);

        Assert.Single((await FindAsync()).Groups);
    }
}
