using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SysScrub.IconGen;

/// <summary>
/// Marka görsellerinin tek kaynağı. Her şey 256x256 tasarım uzayında çizilir ve
/// istenen boyuta ölçeklenir; böylece 16px ile 256px arasında aynı oranlar korunur.
/// </summary>
internal static class BrandArt
{
    private const double Canvas = 256d;

    public static BitmapSource RenderIcon(int pixelSize, Palette palette, GlyphKind glyph, bool inverted = false)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double scale = pixelSize / Canvas;
            dc.PushTransform(new ScaleTransform(scale, scale));

            Color tile = inverted ? palette.Accent : palette.Surface;
            Color mark = inverted ? palette.Ground : palette.Accent;

            DrawTile(dc, tile, inverted ? null : palette.Border);
            DrawGlyph(dc, glyph, mark);

            dc.Pop();
        }

        return Render(visual, pixelSize, pixelSize);
    }

    private static void DrawTile(DrawingContext dc, Color fill, Color? stroke)
    {
        var rect = new Rect(0, 0, Canvas, Canvas);
        var pen = stroke is null ? null : new Pen(new SolidColorBrush(stroke.Value), 4);
        dc.DrawRoundedRectangle(new SolidColorBrush(fill), pen, rect, 52, 52);
    }

    private static void DrawGlyph(DrawingContext dc, GlyphKind glyph, Color color)
    {
        var brush = new SolidColorBrush(color);

        switch (glyph)
        {
            case GlyphKind.Shield:
                dc.DrawGeometry(brush, null, ShieldGeometry());
                break;

            case GlyphKind.ScanRing:
                DrawScanRing(dc, brush);
                break;

            case GlyphKind.Sweep:
                DrawSweep(dc, brush);
                break;
        }
    }

    /// <summary>Kalın halka + üstte açık bir boşluk, merkezde nokta. Uygulamanın tarama halkasıyla aynı dil.</summary>
    private static void DrawScanRing(DrawingContext dc, Brush brush)
    {
        const double cx = 128, cy = 128, radius = 72, thickness = 26;

        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        var arc = new StreamGeometry();
        using (var ctx = arc.Open())
        {
            // -60° başlayıp saat yönünde 300° dönen yay: üstte belirgin bir boşluk kalır.
            Point start = OnCircle(cx, cy, radius, -60);
            Point end = OnCircle(cx, cy, radius, 240);
            ctx.BeginFigure(start, isFilled: false, isClosed: false);
            ctx.ArcTo(end, new Size(radius, radius), 0, isLargeArc: true, SweepDirection.Clockwise, true, false);
        }

        arc.Freeze();
        dc.DrawGeometry(null, pen, arc);
        dc.DrawEllipse(brush, null, new Point(cx, cy), 20, 20);
    }

    /// <summary>Kesik köşeli blok, içinden iki şerit oyulmuş — "süpürülmüş" hissi veren sert bir işaret.</summary>
    private static void DrawSweep(DrawingContext dc, Brush brush)
    {
        var block = new StreamGeometry();
        using (var ctx = block.Open())
        {
            ctx.BeginFigure(new Point(60, 60), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(196, 60), true, false);
            ctx.LineTo(new Point(196, 148), true, false);
            ctx.LineTo(new Point(148, 196), true, false);
            ctx.LineTo(new Point(60, 196), true, false);
        }

        block.Freeze();

        var stripes = new GeometryGroup { FillRule = FillRule.Nonzero };
        stripes.Children.Add(new RectangleGeometry(new Rect(84, 100, 112, 18)));
        stripes.Children.Add(new RectangleGeometry(new Rect(84, 138, 76, 18)));
        stripes.Freeze();

        var mark = new CombinedGeometry(GeometryCombineMode.Exclude, block, stripes);
        mark.Freeze();

        dc.DrawGeometry(brush, null, mark);
    }

    private static Geometry ShieldGeometry()
    {
        var g = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(72, 88), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(72, 82), true, false);
            ctx.ArcTo(new Point(84, 70), new Size(12, 12), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(172, 70), true, false);
            ctx.ArcTo(new Point(184, 82), new Size(12, 12), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(184, 134), true, false);
            ctx.BezierTo(new Point(184, 170), new Point(160, 194), new Point(128, 206), true, false);
            ctx.BezierTo(new Point(96, 194), new Point(72, 170), new Point(72, 134), true, false);
        }

        g.Freeze();
        return g;
    }

    private static Point OnCircle(double cx, double cy, double r, double degrees)
    {
        double rad = degrees * Math.PI / 180d;
        return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
    }

    /// <summary>README banner'ı ve kurulum sihirbazı görselleri için kompozisyon.</summary>
    public static BitmapSource RenderPanel(int width, int height, Palette p, bool withWordmark)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = new LinearGradientBrush(
                p.Ground, p.Surface, new Point(0, 0), new Point(1, 1));
            dc.DrawRectangle(background, null, new Rect(0, 0, width, height));

            // Vurgu renginden çok hafif bir hâle — düz zeminin ölü durmasını engeller.
            DrawGlow(dc, new Point(width * 0.5, height * (withWordmark ? 0.30 : 0.5)),
                Math.Max(width, height) * 0.45, p.Accent, 0.16);

            double glyph = Math.Min(width, height) * (withWordmark ? 0.40 : 0.52);
            double gx = (width - glyph) / 2d;
            double gy = withWordmark ? height * 0.30 - glyph * 0.5 : (height - glyph) / 2d;

            var icon = RenderIcon((int)Math.Max(glyph, 16), p, Palette.SelectedGlyph, Palette.SelectedInverted);
            dc.DrawImage(icon, new Rect(gx, gy, glyph, glyph));

            if (withWordmark)
            {
                var title = Text("SysScrub", Math.Min(width, height) * 0.11, p.Text, FontWeights.SemiBold);
                var subtitle = Text(
                    "Windows bakım, sürücü güncelleme ve disk sağlığı",
                    Math.Min(width, height) * 0.045, p.TextMuted, FontWeights.Normal);

                dc.DrawText(title, new Point((width - title.Width) / 2d, height * 0.60));
                dc.DrawText(subtitle, new Point((width - subtitle.Width) / 2d, height * 0.60 + title.Height + height * 0.02));
            }
        }

        return Render(visual, width, height);
    }

    private static void DrawGlow(DrawingContext dc, Point center, double radius, Color color, double opacity)
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(255 * opacity), color.R, color.G, color.B), 0.0),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.0)
            }
        };

        dc.DrawEllipse(brush, null, center, radius, radius);
    }

    public static BitmapSource Render(DrawingVisual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static FormattedText Text(string value, double size, Color color, FontWeight weight)
    {
        return new FormattedText(
            value,
            CultureInfo.GetCultureInfo("tr-TR"),
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            new SolidColorBrush(color),
            96);
    }
}
