using System.Windows.Controls;
using SysScrub.App.ViewModels;

namespace SysScrub.App.Views.Pages;

public partial class StartupPage : UserControl
{
    public StartupPage()
    {
        InitializeComponent();

        DataContext = App.Resolve<StartupViewModel>();
    }
}
