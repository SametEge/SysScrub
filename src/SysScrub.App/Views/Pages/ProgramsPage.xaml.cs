using System.Windows.Controls;
using SysScrub.App.ViewModels;

namespace SysScrub.App.Views.Pages;

public partial class ProgramsPage : UserControl
{
    public ProgramsPage()
    {
        InitializeComponent();

        DataContext = App.Resolve<ProgramsViewModel>();
    }
}
