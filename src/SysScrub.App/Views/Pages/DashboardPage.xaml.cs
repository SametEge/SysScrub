using System.Windows.Controls;
using SysScrub.App.ViewModels;

namespace SysScrub.App.Views.Pages;

public partial class DashboardPage : UserControl
{
    public DashboardPage()
    {
        InitializeComponent();

        // Sayfa bir DataTemplate içinden oluşturuluyor; bağımlılığını kendisi çözer.
        DataContext = App.Resolve<DashboardViewModel>();
    }
}
