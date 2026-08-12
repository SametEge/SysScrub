using SysScrub.App.Services;
using SysScrub.App.ViewModels;
using Wpf.Ui.Controls;

namespace SysScrub.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly ThemeService _theme;

    public MainWindow(MainWindowViewModel viewModel, ThemeService theme)
    {
        _theme = theme;

        InitializeComponent();

        DataContext = viewModel;

        // Mica ancak pencere tutamacı oluştuktan sonra uygulanabiliyor.
        SourceInitialized += (_, _) => _theme.ApplyBackdrop(this);
    }
}
