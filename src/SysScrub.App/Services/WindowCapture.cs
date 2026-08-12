using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SysScrub.App.Services;

/// <summary>
/// Pencereyi PNG'ye basar. Tasarım incelemesi ve README ekran görüntüleri için;
/// uygulamanın normal akışında kullanılmaz.
///
/// Kullanım:  dotnet SysScrub.dll --screenshot=C:\yol\panel.png
/// exe yerine dll üzerinden çalıştırmak, yönetici manifestini atlayarak UAC istemini önler.
/// </summary>
internal static class WindowCapture
{
    private const string Switch = "--screenshot=";
    private const string PageSwitch = "--page=";

    public static string? PathFromArgs(IReadOnlyList<string> args) => ValueOf(args, Switch);

    /// <summary>Ekran görüntüsü alınırken hangi modülün açık olacağı.</summary>
    public static string? PageFromArgs(IReadOnlyList<string> args) => ValueOf(args, PageSwitch);

    private static string? ValueOf(IReadOnlyList<string> args, string prefix)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[prefix.Length..];
            }
        }

        return null;
    }

    public static void Save(Window window, string path, double scale = 1.5d)
    {
        int width = (int)Math.Round(window.ActualWidth * scale);
        int height = (int)Math.Round(window.ActualHeight * scale);

        if (width <= 0 || height <= 0)
        {
            return;
        }

        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);

        // Mica arkaplanı DWM tarafından çizildiği için RenderTargetBitmap'e düşmez;
        // saydam kalmasın diye zemini altına biz koyuyoruz.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var ground = Application.Current.TryFindResource("SsGround") as Brush ?? Brushes.Black;
            dc.DrawRectangle(ground, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
            dc.DrawRectangle(new VisualBrush(window), null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
        }

        bitmap.Render(visual);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }
}
