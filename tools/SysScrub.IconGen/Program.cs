using System.IO;

namespace SysScrub.IconGen;

internal static class Program
{
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    // WPF çizim nesneleri STA iş parçacığı gerektirir.
    [STAThread]
    private static int Main(string[] args)
    {
        string root = FindRepositoryRoot();

        if (args.Length > 0 && args[0].Equals("variants", StringComparison.OrdinalIgnoreCase))
        {
            string sheet = Path.Combine(root, "docs", "assets", "palette-variants.png");
            ImageWriters.WritePng(sheet, VariantSheet.Render());
            Report(root, sheet, "karşılaştırma sayfası");
            return 0;
        }

        Palette palette = Palette.Selected;
        GlyphKind glyph = Palette.SelectedGlyph;
        bool inverted = Palette.SelectedInverted;

        var frames = IconSizes.Select(size => BrandArt.RenderIcon(size, palette, glyph, inverted)).ToList();

        string icoPath = Path.Combine(root, "src", "SysScrub.App", "Assets", "SysScrub.ico");
        ImageWriters.WriteIco(icoPath, frames);
        Report(root, icoPath, $"{IconSizes.Length} boyut");

        string png256 = Path.Combine(root, "docs", "assets", "icon-256.png");
        ImageWriters.WritePng(png256, BrandArt.RenderIcon(256, palette, glyph, inverted));
        Report(root, png256);

        string banner = Path.Combine(root, "docs", "assets", "banner.png");
        ImageWriters.WritePng(banner, BrandArt.RenderPanel(1280, 440, palette, withWordmark: true));
        Report(root, banner);

        string wizardLarge = Path.Combine(root, "installer", "assets", "wizard-large.bmp");
        ImageWriters.WriteBmp24(wizardLarge, BrandArt.RenderPanel(164, 314, palette, withWordmark: false), palette.Ground);
        Report(root, wizardLarge);

        string wizardSmall = Path.Combine(root, "installer", "assets", "wizard-small.bmp");
        ImageWriters.WriteBmp24(wizardSmall, BrandArt.RenderPanel(55, 58, palette, withWordmark: false), palette.Ground);
        Report(root, wizardSmall);

        Console.WriteLine("Marka görselleri üretildi.");
        return 0;
    }

    private static void Report(string root, string path, string? note = null)
    {
        string suffix = note is null ? string.Empty : $"  ({note})";
        Console.WriteLine($"  {Path.GetRelativePath(root, path)}{suffix}");
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SysScrub.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("SysScrub.sln bulunamadı — araç depo içinden çalıştırılmalı.");
    }
}
