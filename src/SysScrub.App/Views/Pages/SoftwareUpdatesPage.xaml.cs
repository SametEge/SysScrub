using System.Windows.Controls;
using SysScrub.App.ViewModels;

namespace SysScrub.App.Views.Pages;

public partial class SoftwareUpdatesPage : UserControl
{
    public SoftwareUpdatesPage()
    {
        InitializeComponent();

        DataContext = App.Resolve<SoftwareUpdatesViewModel>();
    }
}
