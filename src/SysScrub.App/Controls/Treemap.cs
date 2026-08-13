using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SysScrub.Core.Analysis;

namespace SysScrub.App.Controls;

/// <summary>
/// Klasör boyutlarını alan olarak gösteren treemap.
///
/// Yerleşim <see cref="TreemapLayout"/>'ta: saf geometri olduğu için orada test
/// edilebiliyor. Bu denetim yalnızca çiziyor ve fareyi dinliyor.
///
/// Çizim <c>OnRender</c> içinde tek geçişte yapılıyor; binlerce dikdörtgen için
/// görsel ağaca eleman eklemek (Rectangle nesneleri) hem belleği hem de yerleşim
/// hesabını gereksiz yere şişirirdi.
/// </summary>
public sealed class Treemap : FrameworkElement
{
    public static readonly DependencyProperty RootProperty = DependencyProperty.Register(
        nameof(Root), typeof(FolderNode), typeof(Treemap),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnRootChanged));

    public static readonly DependencyProperty HoveredProperty = DependencyProperty.Register(
        nameof(Hovered), typeof(FolderNode), typeof(Treemap),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(Treemap),
        new FrameworkPropertyMetadata(Brushes.OrangeRed, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(Brush), typeof(Treemap),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Kullanıcı bir klasöre çift tıkladığında tetiklenir.</summary>
    public static readonly RoutedEvent NodeActivatedEvent = EventManager.RegisterRoutedEvent(
        nameof(NodeActivated), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Treemap));

    /// <summary>
    /// Dosya türü grupları. Renkler orta doygunlukta seçildi: hem koyu hem açık
    /// zeminde okunuyorlar. Vurgu rengi bilerek yok — o etkileşim için ayrılmış
    /// ve burada yalnızca imlecin üstünde olduğu bloğun çerçevesinde kullanılıyor.
    /// </summary>
    private static readonly (string[] Extensions, Color Color)[] TypePalette =
    [
        ([".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm"], Color.FromRgb(0x6E, 0x8F, 0xD6)),
        ([".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tga", ".psd"], Color.FromRgb(0x5F, 0xA8, 0x8E)),
        ([".zip", ".rar", ".7z", ".cab", ".iso", ".pak", ".asar"], Color.FromRgb(0xC0, 0x8A, 0x5A)),
        ([".exe", ".dll", ".sys", ".msi", ".so", ".pdb"], Color.FromRgb(0x8B, 0x82, 0xC4)),
        ([".mp3", ".wav", ".flac", ".ogg", ".m4a"], Color.FromRgb(0xB8, 0x74, 0xA8)),
        ([".txt", ".pdf", ".docx", ".xlsx", ".pptx", ".xml", ".json", ".md"], Color.FromRgb(0x77, 0x9A, 0xAA))
    ];

    private static readonly Color FolderColor = Color.FromRgb(0x4A, 0x55, 0x66);
    private static readonly Color OtherColor = Color.FromRgb(0x6B, 0x72, 0x7D);

    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private IReadOnlyList<TreemapTile<FolderNode>> _tiles = [];
    private Size _layoutSize;

    public event RoutedEventHandler NodeActivated
    {
        add => AddHandler(NodeActivatedEvent, value);
        remove => RemoveHandler(NodeActivatedEvent, value);
    }

    public Treemap()
    {
        ClipToBounds = true;
        Focusable = false;
    }

    public FolderNode? Root
    {
        get => (FolderNode?)GetValue(RootProperty);
        set => SetValue(RootProperty, value);
    }

    public FolderNode? Hovered
    {
        get => (FolderNode?)GetValue(HoveredProperty);
        set => SetValue(HoveredProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public Brush LabelBrush
    {
        get => (Brush)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    private static void OnRootChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var treemap = (Treemap)d;

        treemap._tiles = [];
        treemap.Hovered = null;
        treemap.InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        // Arka plan: fare olaylarının gelmesi için saydam da olsa bir dolgu şart.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        if (Root is null || RenderSize.Width <= 0 || RenderSize.Height <= 0)
        {
            return;
        }

        EnsureLayout();

        foreach (TreemapTile<FolderNode> tile in _tiles)
        {
            var rect = new Rect(tile.Bounds.X, tile.Bounds.Y, tile.Bounds.Width, tile.Bounds.Height);

            bool hovered = ReferenceEquals(tile.Item, Hovered);

            context.DrawRectangle(
                new SolidColorBrush(ColorFor(tile.Item)) { Opacity = hovered ? 1.0 : 0.85 },
                null,
                rect);

            // Blok sınırları: ince açık çizgi, komşu blokları ayırıyor.
            if (rect is { Width: > 3, Height: > 3 })
            {
                context.DrawRectangle(
                    null,
                    new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1),
                    rect);
            }

            if (hovered)
            {
                context.DrawRectangle(null, new Pen(AccentBrush, 2), rect);
            }

            DrawLabel(context, tile.Item, rect);
        }
    }

    /// <summary>Etiket yalnızca sığdığında yazılıyor; kırpılmış metin gürültüden başka bir şey değil.</summary>
    private void DrawLabel(DrawingContext context, FolderNode node, Rect rect)
    {
        const double minimumWidth = 74;
        const double minimumHeight = 26;

        if (rect.Width < minimumWidth || rect.Height < minimumHeight)
        {
            return;
        }

        var text = new FormattedText(
            node.Name,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            11.5,
            LabelBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, rect.Width - 12),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };

        context.DrawText(text, new Point(rect.X + 6, rect.Y + 5));

        if (rect.Height < minimumHeight + 16)
        {
            return;
        }

        var size = new FormattedText(
            node.SizeLabel,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            10.5,
            LabelBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, rect.Width - 12),
            MaxLineCount = 1
        };

        context.DrawText(size, new Point(rect.X + 6, rect.Y + 21));
    }

    private void EnsureLayout()
    {
        if (_tiles.Count > 0 && _layoutSize == RenderSize)
        {
            return;
        }

        _layoutSize = RenderSize;

        _tiles = TreemapLayout.Squarify(
            Root!.Children,
            node => node.SizeBytes,
            new TreemapRect(0, 0, RenderSize.Width, RenderSize.Height));
    }

    private static Color ColorFor(FolderNode node)
    {
        if (!node.IsFile)
        {
            return FolderColor;
        }

        // Tam nitelenmiş: bu ad alanında Path, WPF'in şekil sınıfını gösteriyor.
        string extension = System.IO.Path.GetExtension(node.Name);

        foreach ((string[] extensions, Color color) in TypePalette)
        {
            if (extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return color;
            }
        }

        return OtherColor;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        Point position = e.GetPosition(this);
        FolderNode? hit = HitTest(position);

        if (!ReferenceEquals(hit, Hovered))
        {
            Hovered = hit;
            ToolTip = hit is null ? null : $"{hit.FullPath}\n{hit.SizeLabel}";
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        Hovered = null;
        ToolTip = null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (HitTest(e.GetPosition(this)) is { IsFile: false } folder)
        {
            RaiseEvent(new NodeActivatedEventArgs(NodeActivatedEvent, this, folder));
        }
    }

    private FolderNode? HitTest(Point position)
    {
        foreach (TreemapTile<FolderNode> tile in _tiles)
        {
            if (tile.Bounds.Contains(position.X, position.Y))
            {
                return tile.Item;
            }
        }

        return null;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);

        _tiles = [];
        InvalidateVisual();
    }
}

/// <summary>Treemap'te bir klasöre girildiğinde taşınan olay verisi.</summary>
public sealed class NodeActivatedEventArgs(RoutedEvent routedEvent, object source, FolderNode node)
    : RoutedEventArgs(routedEvent, source)
{
    public FolderNode Node { get; } = node;
}
