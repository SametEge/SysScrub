using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SysScrub.IconGen;

/// <summary>
/// Palet ve işaret seçimi için karşılaştırma sayfası üretir: her palet bir satır,
/// satırda gerçek ikon çıktıları ve o paletle çizilmiş bir arayüz parçası.
/// </summary>
internal static class VariantSheet
{
    private const double RowHeight = 260;
    private const double Width = 1340;
    private const double PadX = 36;

    public static BitmapSource Render()
    {
        double height = 96 + Palette.All.Length * RowHeight;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Palette.Hex("07080A")), null, new Rect(0, 0, Width, height));

            var heading = BrandArt.Text("SysScrub — renk ve işaret yönleri", 30, Colors.White, FontWeights.SemiBold);
            dc.DrawText(heading, new Point(PadX, 30));

            var hint = BrandArt.Text(
                "her satır: palet · kalkan · tarama halkası · süpürme bloğu · ters ikon · arayüz parçası",
                15, Palette.Hex("7C838D"), FontWeights.Normal);
            dc.DrawText(hint, new Point(PadX, 68));

            for (int i = 0; i < Palette.All.Length; i++)
            {
                DrawRow(dc, Palette.All[i], 96 + i * RowHeight);
            }
        }

        return BrandArt.Render(visual, (int)Width, (int)height);
    }

    private static void DrawRow(DrawingContext dc, Palette p, double top)
    {
        dc.DrawRectangle(
            new SolidColorBrush(p.Ground), null,
            new Rect(0, top, Width, RowHeight - 8));

        double textX = PadX;
        var name = BrandArt.Text(p.Name, 21, p.Text, FontWeights.SemiBold);
        dc.DrawText(name, new Point(textX, top + 26));

        var character = BrandArt.Text(p.Character, 13.5, p.TextMuted, FontWeights.Normal);
        character.MaxTextWidth = 230;
        dc.DrawText(character, new Point(textX, top + 58));

        DrawSwatches(dc, p, textX, top + 118);

        double iconY = top + 44;
        double x = 300;

        x = DrawIcon(dc, p, GlyphKind.Shield, x, iconY, "kalkan", inverted: false);
        x = DrawIcon(dc, p, GlyphKind.ScanRing, x, iconY, "halka", inverted: false);
        x = DrawIcon(dc, p, GlyphKind.Sweep, x, iconY, "süpürme", inverted: false);
        x = DrawIcon(dc, p, GlyphKind.ScanRing, x, iconY, "ters", inverted: true);

        DrawUiSnippet(dc, p, x + 16, top + 30);
    }

    private static void DrawSwatches(DrawingContext dc, Palette p, double x, double y)
    {
        Color[] colors = [p.Ground, p.Surface, p.SurfaceRaised, p.Border, p.Accent, p.AccentSoft];

        for (int i = 0; i < colors.Length; i++)
        {
            var rect = new Rect(x + i * 34, y, 28, 28);
            dc.DrawRoundedRectangle(
                new SolidColorBrush(colors[i]),
                new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1),
                rect, 6, 6);
        }
    }

    private static double DrawIcon(DrawingContext dc, Palette p, GlyphKind glyph, double x, double y, string label, bool inverted)
    {
        const int big = 96;
        const int small = 32;

        var large = BrandArt.RenderIcon(big, p, glyph, inverted);
        dc.DrawImage(large, new Rect(x, y, big, big));

        // Küçük boyutta hâlâ okunuyor mu — asıl sınav bu.
        var tiny = BrandArt.RenderIcon(small, p, glyph, inverted);
        dc.DrawImage(tiny, new Rect(x + big - small, y + big + 12, small, small));

        var caption = BrandArt.Text(label, 12.5, p.TextMuted, FontWeights.Normal);
        dc.DrawText(caption, new Point(x, y + big + 20));

        return x + big + 34;
    }

    /// <summary>Paletin gerçek arayüzde nasıl durduğunu gösteren küçük kart.</summary>
    private static void DrawUiSnippet(DrawingContext dc, Palette p, double x, double y)
    {
        double w = Width - x - PadX;
        double h = RowHeight - 68;

        dc.DrawRoundedRectangle(
            new SolidColorBrush(p.Surface),
            new Pen(new SolidColorBrush(p.Border), 1),
            new Rect(x, y, w, h), 12, 12);

        var title = BrandArt.Text("Disk sağlığı", 16, p.Text, FontWeights.SemiBold);
        dc.DrawText(title, new Point(x + 20, y + 18));

        var subtitle = BrandArt.Text("Samsung SSD 990 PRO 2TB", 12.5, p.TextMuted, FontWeights.Normal);
        dc.DrawText(subtitle, new Point(x + 20, y + 42));

        // Durum rozeti
        var badgeRect = new Rect(x + 20, y + 72, 74, 26);
        dc.DrawRoundedRectangle(new SolidColorBrush(WithAlpha(p.Accent, 0x33)), null, badgeRect, 13, 13);
        var badge = BrandArt.Text("İYİ", 12.5, p.Accent, FontWeights.SemiBold);
        dc.DrawText(badge, new Point(badgeRect.X + (badgeRect.Width - badge.Width) / 2, badgeRect.Y + 4));

        // İlerleme çubuğu — vurgu renginin dolgu olarak nasıl durduğunu gösterir.
        double barX = x + 112, barY = y + 80, barW = w - 132 - 20, barH = 10;
        dc.DrawRoundedRectangle(new SolidColorBrush(p.SurfaceRaised), null, new Rect(barX, barY, barW, barH), 5, 5);
        dc.DrawRoundedRectangle(new SolidColorBrush(p.Accent), null, new Rect(barX, barY, barW * 0.72, barH), 5, 5);

        // Buton — birincil ve ikincil bir arada.
        var primary = new Rect(x + 20, y + h - 54, 132, 36);
        dc.DrawRoundedRectangle(new SolidColorBrush(p.Accent), null, primary, 8, 8);
        var primaryText = BrandArt.Text("Taramayı başlat", 13, p.Ground, FontWeights.SemiBold);
        dc.DrawText(primaryText, new Point(primary.X + (primary.Width - primaryText.Width) / 2, primary.Y + 9));

        var secondary = new Rect(x + 164, y + h - 54, 104, 36);
        dc.DrawRoundedRectangle(
            new SolidColorBrush(p.SurfaceRaised),
            new Pen(new SolidColorBrush(p.Border), 1), secondary, 8, 8);
        var secondaryText = BrandArt.Text("Ayrıntılar", 13, p.Text, FontWeights.Normal);
        dc.DrawText(secondaryText, new Point(secondary.X + (secondary.Width - secondaryText.Width) / 2, secondary.Y + 9));

        // Sıcaklık okuması — sayısal göstergelerin tonu.
        var temp = BrandArt.Text("42°C", 34, p.Text, FontWeights.SemiBold);
        dc.DrawText(temp, new Point(x + w - temp.Width - 24, y + h - 62));

        var tempLabel = BrandArt.Text("sıcaklık", 12, p.TextMuted, FontWeights.Normal);
        dc.DrawText(tempLabel, new Point(x + w - tempLabel.Width - 24, y + h - 26));
    }

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
}
