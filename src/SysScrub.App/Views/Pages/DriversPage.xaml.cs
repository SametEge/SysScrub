using System.Windows.Controls;
using SysScrub.App.ViewModels;

namespace SysScrub.App.Views.Pages;

public partial class DriversPage : UserControl
{
    public DriversPage()
    {
        InitializeComponent();

        var viewModel = App.Resolve<DriversViewModel>();
        DataContext = viewModel;

        // Envanter okuma birkaç saniye sürüyor; sayfa ilk açıldığında kendiliğinden başlar.
        Loaded += (_, _) =>
        {
            if (!viewModel.HasLoaded && viewModel.LoadCommand.CanExecute(null))
            {
                viewModel.LoadCommand.Execute(null);
            }
        };
    }
}
