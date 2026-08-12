using System.Windows.Media;

namespace SysScrub.IconGen;

/// <summary>Tasarım sisteminin renk kimliği. Themes/Colors.xaml bu değerlerden türetilir.</summary>
internal sealed record Palette(
    string Name,
    string Character,
    Color Ground,
    Color Surface,
    Color SurfaceRaised,
    Color Border,
    Color Text,
    Color TextMuted,
    Color Accent,
    Color AccentSoft)
{
    public static Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromRgb(
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }

    public static readonly Palette Graphite = new(
        "A — Grafit & Sinyal",
        "Ölçüm aleti. Ciddi, endüstriyel, teşhis odaklı.",
        Ground: Hex("0E1013"),
        Surface: Hex("17191D"),
        SurfaceRaised: Hex("1F2226"),
        Border: Hex("2A2E35"),
        Text: Hex("F2F3F5"),
        TextMuted: Hex("9AA1AB"),
        Accent: Hex("FF6B2C"),
        AccentSoft: Hex("FFA366"));

    public static readonly Palette Copper = new(
        "B — Mürekkep & Bakır",
        "Pahalı ve sakin. Sıcak metal, az ama net vurgu.",
        Ground: Hex("101319"),
        Surface: Hex("1A1E27"),
        SurfaceRaised: Hex("232833"),
        Border: Hex("2E3542"),
        Text: Hex("F0F1F4"),
        TextMuted: Hex("959CAA"),
        Accent: Hex("D98A4E"),
        AccentSoft: Hex("F0BE86"));

    public static readonly Palette Lime = new(
        "C — Kömür & Asit Yeşili",
        "Geliştirici aleti. Hızlı, teknik, tazelik çağrışımı.",
        Ground: Hex("0C0F0D"),
        Surface: Hex("151A17"),
        SurfaceRaised: Hex("1D2320"),
        Border: Hex("28312B"),
        Text: Hex("EFF2EF"),
        TextMuted: Hex("94A099"),
        Accent: Hex("8FE04A"),
        AccentSoft: Hex("BAF07E"));

    public static readonly Palette Teal = new(
        "D — Derin Teal & Kum",
        "Klinik ve güven veren. Disk sağlığı ekranına çok yakışır.",
        Ground: Hex("0A1316"),
        Surface: Hex("111E22"),
        SurfaceRaised: Hex("18282D"),
        Border: Hex("21353A"),
        Text: Hex("ECF2F1"),
        TextMuted: Hex("8FA5A6"),
        Accent: Hex("2FBFA4"),
        AccentSoft: Hex("E6D7B8"));

    public static readonly Palette[] All = [Graphite, Copper, Lime, Teal];

    /// <summary>Onaylanan kimlik. Themes/Colors.xaml ve ikon üretimi bunu kullanır.</summary>
    public static Palette Selected => Graphite;

    /// <summary>Tarama halkası — uygulamanın imza görseliyle aynı dil.</summary>
    public static GlyphKind SelectedGlyph => GlyphKind.ScanRing;

    /// <summary>Dolu vurgu kiremidi: küçük boyutta ve koyu görev çubuğunda en okunaklı olan.</summary>
    public static bool SelectedInverted => true;
}

internal enum GlyphKind
{
    /// <summary>Kalkan — koruma/güvenlik çağrışımı.</summary>
    Shield,

    /// <summary>Tarama halkası — uygulamanın imza görseliyle aynı dil.</summary>
    ScanRing,

    /// <summary>Kesik köşeli blok + tarama çizgisi — daha sert, araç gibi.</summary>
    Sweep
}
