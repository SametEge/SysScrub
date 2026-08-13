namespace SysScrub.Core.Analysis;

/// <summary>Treemap'te tek bir dikdörtgen.</summary>
public readonly record struct TreemapRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double Area => Width * Height;

    /// <summary>Uzun kenarın kısa kenara oranı; 1'e yakın olması istenir.</summary>
    public double AspectRatio => Width <= 0 || Height <= 0
        ? double.MaxValue
        : Math.Max(Width / Height, Height / Width);

    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>Yerleştirilmiş öğe.</summary>
public sealed record TreemapTile<T>(T Item, TreemapRect Bounds);

/// <summary>
/// Squarified treemap yerleşimi.
///
/// Naif treemap alanı sırayla dilimler ve uzun ince şeritler üretir; o şeritlerde
/// ne etiket okunur ne de alanlar göz kararı karşılaştırılabilir. Squarified
/// algoritma dikdörtgenleri kareye yakın tutuyor — okunabilirliğin tamamı buna bağlı.
///
/// Yöntem: öğeler büyükten küçüğe sıralanır ve sıradaki öğe mevcut satıra
/// eklendiğinde en kötü en-boy oranı iyileşiyorsa satıra katılır, kötüleşiyorsa
/// satır kapatılıp kalan alanda yenisi açılır.
///
/// Saf geometri: dosya sistemine de arayüze de bağlı değil, bu yüzden test edilebilir.
/// </summary>
public static class TreemapLayout
{
    /// <summary>Bu boyutun altındaki dikdörtgen çizilse de görünmüyor.</summary>
    private const double MinimumSide = 1.0;

    public static IReadOnlyList<TreemapTile<T>> Squarify<T>(
        IReadOnlyList<T> items,
        Func<T, long> sizeSelector,
        TreemapRect bounds)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(sizeSelector);

        var tiles = new List<TreemapTile<T>>();

        if (items.Count == 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return tiles;
        }

        // Sıfır boyutlu öğeler çizilemez ve algoritmayı sıfıra bölmeye götürür.
        (T Item, long Size)[] ordered = items
            .Select(item => (Item: item, Size: sizeSelector(item)))
            .Where(entry => entry.Size > 0)
            .OrderByDescending(entry => entry.Size)
            .ToArray();

        long total = ordered.Sum(entry => entry.Size);

        if (total <= 0)
        {
            return tiles;
        }

        // Boyutlar piksel karesine ölçekleniyor; böylece alan doğrudan boyutu temsil ediyor.
        double scale = bounds.Area / total;

        var remaining = bounds;
        var row = new List<(T Item, double Area)>();
        double rowArea = 0;

        foreach ((T item, long size) in ordered)
        {
            double area = size * scale;
            double shortSide = Math.Min(remaining.Width, remaining.Height);

            if (row.Count > 0 &&
                WorstAspect(row, rowArea + area, shortSide, area) > WorstAspect(row, rowArea, shortSide, null))
            {
                // Sıradaki öğe satırı bozuyor: satırı kapat, kalan alanda devam et.
                remaining = PlaceRow(row, rowArea, remaining, tiles);
                row.Clear();
                rowArea = 0;
            }

            row.Add((item, area));
            rowArea += area;
        }

        if (row.Count > 0)
        {
            PlaceRow(row, rowArea, remaining, tiles);
        }

        return tiles;
    }

    /// <summary>
    /// Satırdaki en kötü en-boy oranı. <paramref name="candidateArea"/> verilirse
    /// o öğe eklenmiş gibi hesaplanır.
    /// </summary>
    private static double WorstAspect<T>(
        List<(T Item, double Area)> row,
        double totalArea,
        double shortSide,
        double? candidateArea)
    {
        if (totalArea <= 0 || shortSide <= 0)
        {
            return double.MaxValue;
        }

        double max = candidateArea ?? 0;
        double min = candidateArea ?? double.MaxValue;

        foreach ((_, double area) in row)
        {
            max = Math.Max(max, area);
            min = Math.Min(min, area);
        }

        if (min <= 0)
        {
            return double.MaxValue;
        }

        double side2 = shortSide * shortSide;
        double total2 = totalArea * totalArea;

        return Math.Max(side2 * max / total2, total2 / (side2 * min));
    }

    /// <summary>Satırı yerleştirir ve geriye kalan alanı döner.</summary>
    private static TreemapRect PlaceRow<T>(
        List<(T Item, double Area)> row,
        double rowArea,
        TreemapRect bounds,
        List<TreemapTile<T>> tiles)
    {
        bool horizontal = bounds.Width >= bounds.Height;

        // Satırın kalınlığı: alanı, uzandığı kenara bölünce çıkıyor.
        double thickness = horizontal
            ? rowArea / bounds.Height
            : rowArea / bounds.Width;

        double offset = 0;

        foreach ((T item, double area) in row)
        {
            double length = thickness > 0 ? area / thickness : 0;

            TreemapRect rect = horizontal
                ? new TreemapRect(bounds.X, bounds.Y + offset, thickness, length)
                : new TreemapRect(bounds.X + offset, bounds.Y, length, thickness);

            if (rect.Width >= MinimumSide && rect.Height >= MinimumSide)
            {
                tiles.Add(new TreemapTile<T>(item, rect));
            }

            offset += length;
        }

        return horizontal
            ? new TreemapRect(bounds.X + thickness, bounds.Y, Math.Max(0, bounds.Width - thickness), bounds.Height)
            : new TreemapRect(bounds.X, bounds.Y + thickness, bounds.Width, Math.Max(0, bounds.Height - thickness));
    }
}
