using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SysScrub.IconGen;

/// <summary>
/// .ico ve .bmp kapsayıcılarını elle yazar. .NET'te çok boyutlu ikon yazan hazır bir API yok;
/// System.Drawing.Icon yalnızca okur.
/// </summary>
internal static class ImageWriters
{
    /// <summary>
    /// Çok boyutlu ikon yazar. 48px ve altı klasik DIB, 64px ve üstü PNG olarak gömülür —
    /// eski araçlar (Inno Setup, kabuk uzantıları) küçük boyutlarda DIB bekleyebiliyor,
    /// büyük boyutlarda PNG dosyayı küçük tutuyor.
    /// </summary>
    public static void WriteIco(string path, IEnumerable<BitmapSource> frames)
    {
        var entries = new List<(int Size, byte[] Data)>();

        foreach (var frame in frames)
        {
            int size = frame.PixelWidth;
            entries.Add((size, size <= 48 ? EncodeDib(frame) : EncodePng(frame)));
        }

        entries.Sort((a, b) => a.Size.CompareTo(b.Size));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);

        w.Write((ushort)0);                  // reserved
        w.Write((ushort)1);                  // type: icon
        w.Write((ushort)entries.Count);

        int offset = 6 + 16 * entries.Count;
        foreach (var (size, data) in entries)
        {
            w.Write((byte)(size >= 256 ? 0 : size));  // 256 => 0
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);                // palet rengi yok
            w.Write((byte)0);                // reserved
            w.Write((ushort)1);              // planes
            w.Write((ushort)32);             // bit depth
            w.Write(data.Length);
            w.Write(offset);
            offset += data.Length;
        }

        foreach (var (_, data) in entries)
        {
            w.Write(data);
        }
    }

    public static void WritePng(string path, BitmapSource source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
    }

    /// <summary>Inno Setup sihirbaz görselleri 24-bit BMP olmak zorunda; saydamlık verilen zemine düzleştirilir.</summary>
    public static void WriteBmp24(string path, BitmapSource source, Color background)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        byte[] pixels = GetBgra32(source, out int sourceStride);

        int rowSize = ((width * 3) + 3) / 4 * 4;
        int imageSize = rowSize * height;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);

        // BITMAPFILEHEADER
        w.Write((byte)'B');
        w.Write((byte)'M');
        w.Write(14 + 40 + imageSize);
        w.Write((ushort)0);
        w.Write((ushort)0);
        w.Write(14 + 40);

        // BITMAPINFOHEADER
        w.Write(40);
        w.Write(width);
        w.Write(height);
        w.Write((ushort)1);
        w.Write((ushort)24);
        w.Write(0);            // BI_RGB
        w.Write(imageSize);
        w.Write(2835);         // ~72 DPI
        w.Write(2835);
        w.Write(0);
        w.Write(0);

        var row = new byte[rowSize];
        for (int y = height - 1; y >= 0; y--)
        {
            Array.Clear(row);
            for (int x = 0; x < width; x++)
            {
                int i = y * sourceStride + x * 4;
                byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];

                row[x * 3 + 0] = Blend(b, background.B, a);
                row[x * 3 + 1] = Blend(g, background.G, a);
                row[x * 3 + 2] = Blend(r, background.R, a);
            }

            w.Write(row);
        }

        static byte Blend(byte foreground, byte background, byte alpha) =>
            (byte)((foreground * alpha + background * (255 - alpha)) / 255);
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        using var ms = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>32-bit alt-üst ters DIB + boş AND maskesi (modern Windows alfa kanalını kullanır).</summary>
    private static byte[] EncodeDib(BitmapSource source)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        byte[] pixels = GetBgra32(source, out int stride);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // BITMAPINFOHEADER — yükseklik iki katı: XOR görüntüsü + AND maskesi.
        w.Write(40);
        w.Write(width);
        w.Write(height * 2);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(0);
        w.Write(stride * height);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);

        for (int y = height - 1; y >= 0; y--)
        {
            w.Write(pixels, y * stride, stride);
        }

        int maskStride = (width + 31) / 32 * 4;
        w.Write(new byte[maskStride * height]);

        return ms.ToArray();
    }

    private static byte[] GetBgra32(BitmapSource source, out int stride)
    {
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }
}
