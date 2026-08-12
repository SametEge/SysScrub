using System.Windows.Controls;
using SysScrub.App.ViewModels;

namespace SysScrub.App.Views.Pages;

public partial class RegistryPage : UserControl
{
    public RegistryPage()
    {
        InitializeComponent();

        DataContext = App.Resolve<RegistryViewModel>();
    }
}
