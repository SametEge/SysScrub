using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SysScrub.App.Converters;

/// <summary>true → Visible, false → Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>Boş olmayan metin → Visible. Boş etiketlerin yer kaplamasını engeller.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Boş metin → Visible. Arama kutusunun yer tutucu yazısı için.</summary>
public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Sayı sıfırdan büyükse Visible.</summary>
public sealed class PositiveToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double number = value switch
        {
            long l => l,
            int i => i,
            double d => d,
            _ => 0
        };

        return number > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Risk seviyesini fırça anahtarına çevirir. Vurgu rengiyle karışmaması için
/// yalnızca durum renkleri kullanılır.
/// </summary>
public sealed class RiskBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            Core.Rules.RiskLevel.Caution => "SsCaution",
            Core.Rules.RiskLevel.Advanced => "SsDanger",
            _ => "SsTextSecondary"
        };

        return Application.Current?.TryFindResource(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Durum anahtarını ("good" / "caution" / "danger" / "none") fırçaya çevirir.
/// Vurgu rengi kullanılmıyor: turuncu etkileşim için ayrılmış, durum için değil.
/// </summary>
public sealed class SeverityBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = (value as string) switch
        {
            "good" => "SsGood",
            "caution" => "SsCaution",
            "danger" => "SsDanger",
            _ => "SsText"
        };

        return Application.Current?.TryFindResource(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Durum anahtarını rozet dolgusuna çevirir.</summary>
public sealed class SeverityTintConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = (value as string) switch
        {
            "good" => "SsGoodTint",
            "caution" => "SsCautionTint",
            "danger" => "SsDangerTint",
            _ => "SsSurfaceHover"
        };

        return Application.Current?.TryFindResource(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>0–1 arası oranı, parametredeki toplam genişliğe göre piksele çevirir.</summary>
public sealed class FractionWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double fraction = value is double d ? d : 0;
        double total = parameter is string text &&
                       double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : 0;

        return Math.Clamp(fraction, 0, 1) * total;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Risk seviyesini arka plan tonuna çevirir (rozet dolgusu).</summary>
public sealed class RiskTintConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            Core.Rules.RiskLevel.Caution => "SsCautionTint",
            Core.Rules.RiskLevel.Advanced => "SsDangerTint",
            _ => "SsSurfaceHover"
        };

        return Application.Current?.TryFindResource(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
