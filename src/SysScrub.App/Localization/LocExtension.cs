using System.Windows.Data;
using System.Windows.Markup;

namespace SysScrub.App.Localization;

/// <summary>
/// XAML'de çevrilmiş metin: <c>Text="{loc:Str Nav_Dashboard}"</c>.
///
/// Doğrudan dize döndürmek yerine bağlama üretiyor. Sebebi canlı dil değişimi:
/// dize dönseydi metin bir kez yazılır ve dil değişince ekranda eski dil kalırdı.
/// Bağlama, servis "Item[]" bildirimi gönderdiğinde kendini yeniliyor.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class StrExtension : MarkupExtension
{
    public StrExtension()
    {
    }

    public StrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
        {
            return string.Empty;
        }

        return new Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay
        }.ProvideValue(serviceProvider);
    }
}
