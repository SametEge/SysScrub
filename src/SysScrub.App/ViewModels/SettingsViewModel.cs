using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SysScrub.App.Services;
using SysScrub.Core.Cleaning;
using SysScrub.Core.Formatting;
using SysScrub.Core.Machine;
using SysScrub.Core.Settings;

namespace SysScrub.App.ViewModels;

/// <summary>
/// Ayarlar ekranı.
///
/// Her ayar bir şey yapıyor: gösterilip de bağlanmamış tek bir kontrol yok.
/// Yapamadığımız şeyleri de gizlemiyoruz — altı dil planlanmışken bugün yalnızca
/// Türkçe var ve ekran bunu açıkça yazıyor.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly ThemeService _theme;
    private readonly QuarantineStore _quarantine;
    private readonly ScheduledMaintenance _maintenance;
    private readonly SystemInfoService _systemInfo;
    private readonly ILogger<SettingsViewModel> _logger;

    private bool _applying;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private long _quarantineBytes;

    [ObservableProperty]
    private int _quarantineRuns;

    [ObservableProperty]
    private MaintenanceTaskState _maintenanceState = MaintenanceTaskState.Missing;

    public SettingsViewModel(
        SettingsStore store,
        ThemeService theme,
        QuarantineStore quarantine,
        ScheduledMaintenance maintenance,
        SystemInfoService systemInfo,
        ILogger<SettingsViewModel> logger)
    {
        _store = store;
        _theme = theme;
        _quarantine = quarantine;
        _maintenance = maintenance;
        _systemInfo = systemInfo;
        _logger = logger;

        IsElevated = systemInfo.Capture().IsElevated;

        Refresh();
    }

    // ------------------------------------------------------------------ görünüm

    public bool IsSystemTheme => _store.Current.Theme == ThemePreference.System;

    public bool IsLightTheme => _store.Current.Theme == ThemePreference.Light;

    public bool IsDarkTheme => _store.Current.Theme == ThemePreference.Dark;

    [RelayCommand]
    private void SetTheme(string preference)
    {
        ThemePreference theme = preference switch
        {
            "light" => ThemePreference.Light,
            "dark" => ThemePreference.Dark,
            _ => ThemePreference.System
        };

        _store.Update(s => s with { Theme = theme });
        _theme.Mode = ToThemeMode(theme);

        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    internal static AppThemeMode ToThemeMode(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => AppThemeMode.Light,
        ThemePreference.Dark => AppThemeMode.Dark,
        _ => AppThemeMode.System
    };

    // ------------------------------------------------------------------ karantina

    public int RetentionDays
    {
        get => _store.Current.QuarantineRetentionDays;
        set
        {
            if (_applying || value == _store.Current.QuarantineRetentionDays)
            {
                return;
            }

            _store.Update(s => s with { QuarantineRetentionDays = value });

            OnPropertyChanged();
            OnPropertyChanged(nameof(RetentionLabel));
        }
    }

    public string RetentionLabel => $"{RetentionDays} gün";

    public string QuarantineSummary => QuarantineRuns == 0
        ? "Karantinada bekleyen dosya yok."
        : $"{QuarantineRuns} çalıştırma  ·  {ByteSize.Format(QuarantineBytes)}";

    public bool HasQuarantine => QuarantineRuns > 0;

    public string QuarantinePath => _quarantine.RootDirectory;

    [RelayCommand]
    private void PurgeQuarantine()
    {
        int removed = _quarantine.Purge(_store.Current.QuarantineRetention);

        StatusMessage = removed == 0
            ? $"Saklama süresini ({RetentionLabel}) aşan kayıt yok."
            : $"{removed} eski karantina kaydı kalıcı olarak silindi.";

        Refresh();
    }

    // ------------------------------------------------------------------ güvenlik

    public bool CreateRestorePoint
    {
        get => _store.Current.CreateRestorePoint;
        set
        {
            if (_applying || value == _store.Current.CreateRestorePoint)
            {
                return;
            }

            _store.Update(s => s with { CreateRestorePoint = value });
            OnPropertyChanged();
        }
    }

    // ------------------------------------------------------------------ zamanlanmış bakım

    public bool ScheduledCleanup
    {
        get => MaintenanceState.Exists;
        set
        {
            if (_applying || value == MaintenanceState.Exists)
            {
                return;
            }

            MaintenanceState = value
                ? _maintenance.Register(_store.Current.ScheduledHour)
                : _maintenance.Remove();

            _store.Update(s => s with { ScheduledCleanup = MaintenanceState.Exists });

            StatusMessage = MaintenanceState.Message
                ?? (MaintenanceState.Exists
                    ? $"Haftalık bakım görevi kuruldu: her pazar saat {ScheduledHour:00}:00."
                    : "Haftalık bakım görevi kaldırıldı.");

            OnPropertyChanged();
            RaiseMaintenanceDerived();
        }
    }

    public int ScheduledHour
    {
        get => _store.Current.ScheduledHour;
        set
        {
            if (_applying || value == _store.Current.ScheduledHour)
            {
                return;
            }

            _store.Update(s => s with { ScheduledHour = value });

            // Görev zaten kuruluysa saatiyle birlikte yeniden yazılmalı.
            if (MaintenanceState.Exists)
            {
                MaintenanceState = _maintenance.Register(value);
            }

            OnPropertyChanged();
            RaiseMaintenanceDerived();
        }
    }

    public string ScheduleLabel => $"Her pazar saat {ScheduledHour:00}:00";

    public string MaintenanceDetail
    {
        get
        {
            if (!MaintenanceState.Exists)
            {
                return "Kapalı. Açıldığında yalnızca güvenli işaretli kurallar çalışır; " +
                       "her silme karantinaya alınır ve Zaman tüneli'nden geri alınabilir.";
            }

            return MaintenanceState.NextRun is { } next
                ? $"Sıradaki çalışma: {next:dd MMMM yyyy HH:mm}"
                : ScheduleLabel;
        }
    }

    /// <summary>Görev oluşturmak yönetici hakkı istiyor; olmadan düğmeyi açmak yanıltıcı olur.</summary>
    public bool CanSchedule => IsElevated && ScheduledMaintenance.CliPath is not null;

    public string ScheduleBlockedReason
    {
        get
        {
            if (!IsElevated)
            {
                return "Zamanlanmış görev oluşturmak için uygulamayı yönetici olarak açın.";
            }

            return ScheduledMaintenance.CliPath is null
                ? "Komut satırı sürümü (sysscrub-cli.exe) bu kurulumda bulunamadı."
                : string.Empty;
        }
    }

    public bool HasScheduleBlockedReason => ScheduleBlockedReason.Length > 0;

    // ------------------------------------------------------------------ veri ve gizlilik

    public string DataDirectory => AppPaths.DataDirectory;

    public string SettingsFilePath => _store.FilePath;

    public bool IsPortable => AppPaths.IsPortable;

    public string StorageModeLabel => AppPaths.IsPortable
        ? "Portatif mod: tüm veri uygulamanın kendi klasöründe, sisteme hiçbir şey yazılmıyor."
        : "Veriler ortak uygulama verisi klasöründe tutuluyor.";

    [RelayCommand]
    private void OpenDataFolder() => OpenFolder(AppPaths.DataDirectory);

    [RelayCommand]
    private void OpenLogsFolder() => OpenFolder(AppPaths.LogsDirectory);

    [RelayCommand]
    private void OpenQuarantineFolder() => OpenFolder(_quarantine.RootDirectory);

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "Klasör açılamadı: {Path}", path);
            StatusMessage = $"Klasör açılamadı: {path}";
        }
    }

    // ------------------------------------------------------------------ hakkında

    public string VersionLabel =>
        typeof(SettingsViewModel).Assembly.GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "bilinmiyor";

    public string SystemLabel
    {
        get
        {
            SystemSnapshot snapshot = _systemInfo.Capture();

            return $"{snapshot.OperatingSystem}  ·  {(IsElevated ? "yönetici" : "sınırlı yetki")}";
        }
    }

    /// <summary>
    /// Dil desteği planlı ama henüz yok. Boş bir açılır liste koymaktansa
    /// durumu yazıyoruz: olmayan bir özelliği varmış gibi göstermek kötü.
    /// </summary>
    public string LanguageNotice =>
        "Arayüz şu an yalnızca Türkçe. İngilizce, Almanca, Japonca, Korece ve " +
        "Basitleştirilmiş Çince çeviriler sonraki sürümde geliyor.";

    // ------------------------------------------------------------------ tazeleme

    [RelayCommand]
    private void Refresh()
    {
        _applying = true;

        try
        {
            QuarantineRuns = _quarantine.List().Count;
            QuarantineBytes = _quarantine.TotalBytes();
            MaintenanceState = _maintenance.Query();

            OnPropertyChanged(nameof(RetentionDays));
            OnPropertyChanged(nameof(RetentionLabel));
            OnPropertyChanged(nameof(CreateRestorePoint));
            OnPropertyChanged(nameof(ScheduledCleanup));
            OnPropertyChanged(nameof(ScheduledHour));
            OnPropertyChanged(nameof(IsSystemTheme));
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsDarkTheme));

            RaiseMaintenanceDerived();
        }
        finally
        {
            _applying = false;
        }
    }

    private void RaiseMaintenanceDerived()
    {
        OnPropertyChanged(nameof(ScheduleLabel));
        OnPropertyChanged(nameof(MaintenanceDetail));
        OnPropertyChanged(nameof(CanSchedule));
        OnPropertyChanged(nameof(ScheduleBlockedReason));
        OnPropertyChanged(nameof(HasScheduleBlockedReason));
    }

    partial void OnQuarantineRunsChanged(int value)
    {
        OnPropertyChanged(nameof(QuarantineSummary));
        OnPropertyChanged(nameof(HasQuarantine));
    }

    partial void OnQuarantineBytesChanged(long value) => OnPropertyChanged(nameof(QuarantineSummary));
}
