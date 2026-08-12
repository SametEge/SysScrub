using System.Windows.Controls;
using SysScrub.App.ViewModels;

namespace SysScrub.App.Views.Pages;

public partial class TimelinePage : UserControl
{
    public TimelinePage()
    {
        InitializeComponent();

        var viewModel = App.Resolve<TimelineViewModel>();
        DataContext = viewModel;

        // Sayfaya her girişte tazelenir: başka bir ekranda yapılan temizlik burada görünsün.
        Loaded += (_, _) => viewModel.Refresh();
    }
}
