using SysScrub.Core.Analysis;
using Xunit;

namespace SysScrub.Core.Tests.Analysis;

/// <summary>
/// Squarified treemap yerleşimi.
///
/// İki şey doğru olmak zorunda: her bloğun alanı boyutuyla orantılı olmalı
/// (yoksa görsel yalan söyler) ve bloklar kareye yakın kalmalı (yoksa etiketler
/// okunmaz ve alanlar göz kararı karşılaştırılamaz).
/// </summary>
public sealed class TreemapLayoutTests
{
    private static readonly TreemapRect Canvas = new(0, 0, 800, 600);

    private static IReadOnlyList<TreemapTile<long>> Layout(params long[] sizes) =>
        TreemapLayout.Squarify(sizes, size => size, Canvas);

    [Fact]
    public void BosListeBosYerlesimUretir() => Assert.Empty(Layout());

    [Fact]
    public void TekOgeTumAlaniKaplar()
    {
        TreemapTile<long> tile = Assert.Single(Layout(1000));

        Assert.Equal(Canvas.Area, tile.Bounds.Area, precision: 3);
    }

    /// <summary>Görselin tek iddiası bu: alan boyutu temsil ediyor.</summary>
    [Fact]
    public void AlanlarBoyutlarlaOrantilidir()
    {
        IReadOnlyList<TreemapTile<long>> tiles = Layout(500, 300, 200);

        double total = tiles.Sum(t => t.Bounds.Area);

        Assert.Equal(0.5, tiles.Single(t => t.Item == 500).Bounds.Area / total, precision: 2);
        Assert.Equal(0.3, tiles.Single(t => t.Item == 300).Bounds.Area / total, precision: 2);
        Assert.Equal(0.2, tiles.Single(t => t.Item == 200).Bounds.Area / total, precision: 2);
    }

    [Fact]
    public void ToplamAlanTuvaliDoldurur()
    {
        IReadOnlyList<TreemapTile<long>> tiles = Layout(400, 300, 200, 100);

        Assert.Equal(Canvas.Area, tiles.Sum(t => t.Bounds.Area), precision: 1);
    }

    [Fact]
    public void BloklarTuvalinDisinaTasmaz()
    {
        foreach (TreemapTile<long> tile in Layout(500, 300, 200, 120, 80, 50, 30, 20, 10, 5))
        {
            Assert.True(tile.Bounds.X >= -0.001, $"X={tile.Bounds.X}");
            Assert.True(tile.Bounds.Y >= -0.001, $"Y={tile.Bounds.Y}");
            Assert.True(tile.Bounds.Right <= Canvas.Width + 0.001, $"Right={tile.Bounds.Right}");
            Assert.True(tile.Bounds.Bottom <= Canvas.Height + 0.001, $"Bottom={tile.Bounds.Bottom}");
        }
    }

    [Fact]
    public void BloklarBirbiriyleOrtusmez()
    {
        TreemapTile<long>[] tiles = Layout(500, 300, 200, 120, 80, 50).ToArray();

        for (int i = 0; i < tiles.Length; i++)
        {
            for (int j = i + 1; j < tiles.Length; j++)
            {
                Assert.False(Overlaps(tiles[i].Bounds, tiles[j].Bounds), $"{i} ile {j} örtüşüyor");
            }
        }
    }

    private static bool Overlaps(TreemapRect a, TreemapRect b) =>
        a.X < b.Right - 0.001 && b.X < a.Right - 0.001 &&
        a.Y < b.Bottom - 0.001 && b.Y < a.Bottom - 0.001;

    /// <summary>
    /// Squarified algoritmanın varlık sebebi. Naif dilimleme burada 10'un
    /// üzerinde oranlar üretiyor ve bloklar okunmaz şeritlere dönüşüyor.
    /// </summary>
    [Fact]
    public void BloklarKareyeYakinKalir()
    {
        IReadOnlyList<TreemapTile<long>> tiles = Layout(500, 400, 300, 250, 200, 150, 120, 100, 80, 60);

        foreach (TreemapTile<long> tile in tiles)
        {
            Assert.True(tile.Bounds.AspectRatio < 5, $"En-boy oranı {tile.Bounds.AspectRatio:F1}");
        }
    }

    [Fact]
    public void EsitBoyutlarEsitAlanAlir()
    {
        IReadOnlyList<TreemapTile<long>> tiles = TreemapLayout.Squarify(
            [100L, 100L, 100L, 100L], size => size, new TreemapRect(0, 0, 400, 400));

        double first = tiles[0].Bounds.Area;

        foreach (TreemapTile<long> tile in tiles)
        {
            Assert.Equal(first, tile.Bounds.Area, precision: 1);
        }
    }

    /// <summary>Sıfır boyutlu öğe çizilemez ve algoritmayı sıfıra bölmeye götürür.</summary>
    [Fact]
    public void SifirBoyutluOgelerAtlanir()
    {
        IReadOnlyList<TreemapTile<long>> tiles = Layout(100, 0, 50, 0);

        Assert.Equal(2, tiles.Count);
        Assert.DoesNotContain(tiles, t => t.Item == 0);
    }

    [Fact]
    public void TumOgelerSifirsaYerlesimBos() => Assert.Empty(Layout(0, 0, 0));

    [Fact]
    public void SifirGenislikliTuvalBosDoner() =>
        Assert.Empty(TreemapLayout.Squarify([100L], size => size, new TreemapRect(0, 0, 0, 600)));

    /// <summary>Çizilemeyecek kadar küçük blok listeye girmiyor; boşuna çizim yapılmıyor.</summary>
    [Fact]
    public void GorunmeyecekKadarKucukBloklarAtlanir()
    {
        var sizes = new long[500];
        sizes[0] = 1_000_000;

        for (int i = 1; i < sizes.Length; i++)
        {
            sizes[i] = 1;
        }

        IReadOnlyList<TreemapTile<long>> tiles = TreemapLayout.Squarify(
            sizes, size => size, new TreemapRect(0, 0, 200, 200));

        Assert.True(tiles.Count < sizes.Length);
    }

    [Fact]
    public void BuyukOgeIlkSirayaGelir()
    {
        IReadOnlyList<TreemapTile<long>> tiles = Layout(100, 900, 300);

        // Sıralama algoritmanın parçası: girdinin sırası önemsiz olmalı.
        Assert.Equal(900, tiles[0].Item);
    }
}
