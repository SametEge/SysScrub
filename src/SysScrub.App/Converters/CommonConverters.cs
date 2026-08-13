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
