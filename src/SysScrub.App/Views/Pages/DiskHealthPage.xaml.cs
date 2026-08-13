using System.Windows.Controls;
using SysScrub.App.ViewModels;

namespace SysScrub.App.Views.Pages;

public partial class DiskHealthPage : UserControl
{
    public DiskHealthPage()
    {
        InitializeComponent();

        DataContext = App.Resolve<DiskHealthViewModel>();
    }
}
