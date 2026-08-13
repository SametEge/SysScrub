using SysScrub.App.Services;
using SysScrub.App.ViewModels;
using Wpf.Ui.Controls;

namespace SysScrub.App.Views;

/// <summary>
/// İlk açılış turu. Ana pencereden önce açılıyor: kullanıcı dilini seçmeden
/// arayüzü görmesin, sonra dilin değişmesiyle her şey yeniden çizilmesin.
/// </summary>
public partial class WelcomeWindow : FluentWindow
{
    private readonly WelcomeViewModel _viewModel;

    public WelcomeWindow(WelcomeViewModel viewModel, ThemeService theme)
    {
        _viewModel = viewModel;

        InitializeComponent();

        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;

        Loaded += (_, _) => theme.ApplyBackdrop(this);
    }

    /// <summary>Kullanıcı turu bitirdi mi; pencereyi çarpıyla kapatmak bitirmek sayılmaz.</summary>
    public bool Completed => _viewModel.Completed;

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        _viewModel.CloseRequested -= OnCloseRequested;

        DialogResult = true;
        Close();
    }
}
