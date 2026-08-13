using System.IO;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SysScrub.App.Localization;
using SysScrub.Core.Settings;
using SysScrub.Core.Updates;
using static SysScrub.App.Localization.L;

namespace SysScrub.App.ViewModels;

/// <summary>
/// Uygulamanın kendi güncellemesi.
///
/// Ayarlardaki karttan sürülüyor. Durum metni saklanmıyor, her seferinde
/// duruma göre üretiliyor: dil değişince cümle de değişsin.
/// </summary>
public sealed partial class AppUpdateViewModel : ObservableObject
{
    /// <summary>Otomatik denetim aralığı. Günde bir yeterli; GitHub'ı boşuna yormuyoruz.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly UpdateService _updates;
    private readonly SettingsStore _store;
    private readonly ILogger<AppUpdateViewModel> _logger;

    private GitHubRelease? _release;
    private DownloadedUpdate? _downloaded;
    private string _failureReason = string.Empty;

    [ObservableProperty]
    private UpdateStatus _status = UpdateStatus.Unknown;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private bool _isInstalling;

    public AppUpdateViewModel(UpdateService updates, SettingsStore store, ILogger<AppUpdateViewModel> logger)
    {
        _updates = updates;
        _store = store;
        _logger = logger;

        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    // ------------------------------------------------------------------ görünen durum

    public string CurrentVersion => UpdateService.Current.ToString();

    public bool AutoCheck
    {
        get => _store.Current.AutoCheckUpdates;
        set
        {
            if (value == _store.Current.AutoCheckUpdates)
            {
                return;
            }

            _store.Update(s => s with { AutoCheckUpdates = value });
            OnPropertyChanged();
        }
    }

    /// <summary>Kartın ana cümlesi. Ne bulunduğunu ya da neden bulunamadığını söyler.</summary>
    public string StatusMessage
    {
        get
        {
            if (IsInstalling)
            {
                return T("Set_Update_Installing");
            }

            if (IsDownloading)
            {
                return T("Set_Update_Downloading", ProgressPercent.ToString("0"));
            }

            if (IsChecking)
            {
                return T("Set_Update_Checking");
            }

            return Status switch
            {
                UpdateStatus.UpToDate => T("Set_Update_UpToDate", CurrentVersion),
                UpdateStatus.Available => T("Set_Update_Available", _release!.Version.ToString()),
                UpdateStatus.AvailableWithoutSetup => T("Set_Update_NoSetup", _release!.Version.ToString()),
                UpdateStatus.Failed => T("Set_Update_Failed", _failureReason),
                _ => LastCheckLabel
            };
        }
    }

    private string LastCheckLabel => _store.Current.LastUpdateCheck is { } last
        ? T("Set_Update_LastCheck", last.LocalDateTime.ToString("dd MMMM yyyy HH:mm"))
        : T("Set_Update_Never");

    /// <summary>İndirilen paketin doğrulama sonucu; boşsa gösterilmiyor.</summary>
    public string VerificationNotice => _downloaded?.Verdict switch
    {
        ChecksumVerdict.Verified => T("Set_Update_Verified"),
        ChecksumVerdict.NotPublished => T("Set_Update_Unverified"),
        _ => string.Empty
    };

    public bool HasVerificationNotice => VerificationNotice.Length > 0;

    public string? ReleaseNotes => _release?.Notes is { Length: > 0 } notes
        ? notes.Length > 600 ? notes[..600].TrimEnd() + "…" : notes
        : null;

    public bool HasReleaseNotes => ReleaseNotes is not null;

    /// <summary>Portatif kurulumda setup çalıştırmak yanlış olur; kullanıcı bunu bilsin.</summary>
    public string PortableNotice =>
        UpdateService.CanInstallInPlace ? string.Empty : T("Set_Update_Portable");

    public bool HasPortableNotice => PortableNotice.Length > 0;

    public bool IsBusy => IsChecking || IsDownloading || IsInstalling;

    public bool HasUpdate => Status is UpdateStatus.Available or UpdateStatus.AvailableWithoutSetup;

    public bool CanDownload =>
        Status == UpdateStatus.Available && UpdateService.CanInstallInPlace && !IsBusy && _downloaded is null;

    public bool CanInstall => _downloaded is not null && !IsBusy;

    // ------------------------------------------------------------------ komutlar

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        IsChecking = true;

        try
        {
            UpdateCheckResult result = await _updates.CheckAsync(cancellationToken);

            Apply(result);
        }
        catch (OperationCanceledException)
        {
            // Kullanıcı sayfadan çıktı; sessizce bırakıyoruz.
        }
        finally
        {
            IsChecking = false;
        }
    }

    private bool CanCheck() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync(CancellationToken cancellationToken)
    {
        if (_release is null)
        {
            return;
        }

        IsDownloading = true;
        ProgressPercent = 0;

        var progress = new Progress<DownloadProgress>(p =>
        {
            if (p.Fraction is { } fraction)
            {
                ProgressPercent = fraction * 100;
            }
        });

        try
        {
            _downloaded = await _updates.DownloadAsync(_release, progress, cancellationToken);

            _logger.LogInformation(
                "Güncelleme indirildi: {File} ({Verdict})",
                _downloaded.FilePath,
                _downloaded.Verdict);
        }
        catch (OperationCanceledException)
        {
            _downloaded = null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
        {
            _logger.LogError(ex, "Güncelleme indirilemedi");

            _downloaded = null;
            _failureReason = ex is InvalidDataException ? T("Set_Update_Mismatch") : ex.Message;
            Status = UpdateStatus.Failed;
        }
        finally
        {
            IsDownloading = false;
            RaiseDerived();
        }
    }

    /// <summary>
    /// Kurulumu başlatır ve uygulamayı kapatır. Kendi dosyalarımızı kilitli
    /// tutarsak kurulum yerlerine yazamaz; kapanmak isteğe bağlı değil.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private void Install()
    {
        if (_downloaded is null)
        {
            return;
        }

        IsInstalling = true;
        RaiseDerived();

        if (!_updates.StartInstaller(_downloaded))
        {
            IsInstalling = false;
            _failureReason = T("Set_Update_LaunchFailed");
            Status = UpdateStatus.Failed;
            RaiseDerived();

            return;
        }

        Application.Current?.Shutdown();
    }

    [RelayCommand]
    private void OpenReleasePage() => _updates.OpenReleasePage(_release);

    // ------------------------------------------------------------------ açılış denetimi

    /// <summary>
    /// Açılışta arka planda çalışır. Ayar kapalıysa ya da son denetimden bu yana
    /// bir gün geçmediyse ağa hiç çıkılmaz.
    /// </summary>
    public async Task CheckOnStartupAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = _store.Current;

        if (!settings.AutoCheckUpdates)
        {
            return;
        }

        if (settings.LastUpdateCheck is { } last && DateTimeOffset.UtcNow - last < CheckInterval)
        {
            return;
        }

        try
        {
            Apply(await _updates.CheckAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            // Uygulama kapanıyor.
        }
    }

    private void Apply(UpdateCheckResult result)
    {
        Status = result.Status;
        _release = result.Release;
        _failureReason = result.Message;

        // Başarısız denetim zaman damgasını ilerletmiyor: ağ geri geldiğinde
        // bir gün beklemeden yeniden bakılsın.
        if (result.Status != UpdateStatus.Failed)
        {
            _store.Update(s => s with { LastUpdateCheck = DateTimeOffset.UtcNow });
        }

        if (result.Status != UpdateStatus.Available)
        {
            _downloaded = null;
        }

        RaiseDerived();
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(VerificationNotice));
        OnPropertyChanged(nameof(HasVerificationNotice));
        OnPropertyChanged(nameof(ReleaseNotes));
        OnPropertyChanged(nameof(HasReleaseNotes));

        CheckCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
    }

    partial void OnStatusChanged(UpdateStatus value) => RaiseDerived();

    partial void OnIsCheckingChanged(bool value) => RaiseDerived();

    partial void OnIsDownloadingChanged(bool value) => RaiseDerived();

    partial void OnIsInstallingChanged(bool value) => RaiseDerived();

    partial void OnProgressPercentChanged(double value) => OnPropertyChanged(nameof(StatusMessage));
}
