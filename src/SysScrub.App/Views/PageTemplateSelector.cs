using System.Windows;
using System.Windows.Controls;
using SysScrub.App.ViewModels;

namespace SysScrub.App.Views;

/// <summary>
/// Seçili modüle göre sayfa şablonunu seçer. Şablonlar Themes/Templates.xaml içinde
/// anahtarlarıyla durur; yeni bir modül tamamlandığında tek yapılacak şey
/// NavigationItem.TemplateKey değerini o modülün şablonuna çevirmek.
/// </summary>
public sealed class PageTemplateSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is not NavigationItem navigationItem || container is not FrameworkElement element)
        {
            return null;
        }

        return element.TryFindResource(navigationItem.TemplateKey) as DataTemplate
               ?? element.TryFindResource("PlaceholderPageTemplate") as DataTemplate;
    }
}
