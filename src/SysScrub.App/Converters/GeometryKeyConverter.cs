using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SysScrub.App.Converters;

/// <summary>
/// Themes/Icons.xaml içindeki bir Geometry anahtarını gerçek Geometry'ye çevirir.
/// Böylece görünüm modelleri WPF tiplerine bağımlı olmadan sadece anahtar taşır.
/// </summary>
public sealed class GeometryKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrEmpty(key))
        {
            return null;
        }

        return Application.Current?.TryFindResource(key) as Geometry;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
