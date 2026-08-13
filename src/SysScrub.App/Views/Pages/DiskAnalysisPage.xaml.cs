using System.Windows;
using System.Windows.Controls;
using SysScrub.App.Controls;
using SysScrub.App.ViewModels;

namespace SysScrub.App.Views.Pages;

public partial class DiskAnalysisPage : UserControl
{
    public DiskAnalysisPage()
    {
        InitializeComponent();

        DataContext = App.Resolve<DiskAnalysisViewModel>();
    }

    /// <summary>
    /// Treemap'te bir klasöre tıklandı. Denetim olay gönderiyor, görünüm modeli
    /// komutu çalıştırıyor — denetimin görünüm modelini tanımasına gerek yok.
    /// </summary>
    private void OnNodeActivated(object sender, RoutedEventArgs e)
    {
        if (e is NodeActivatedEventArgs args && DataContext is DiskAnalysisViewModel viewModel)
        {
            viewModel.OpenCommand.Execute(args.Node);
        }
    }
}
