using System.Windows.Controls;
using SysScrub.App.ViewModels;

namespace SysScrub.App.Views.Pages;

public partial class CleanerPage : UserControl
{
    public CleanerPage()
    {
        InitializeComponent();

        // Görünüm modeli tekil: sayfalar arasında gidip gelirken tarama sonucu korunur.
        DataContext = App.Resolve<CleanerViewModel>();
    }
}
