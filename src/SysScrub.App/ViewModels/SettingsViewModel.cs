using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using static SysScrub.App.Localization.L;
using SysScrub.App.Localization;
using SysScrub.App.Services;
using SysScrub.Core.Cleaning;
using SysScrub.Core.Formatting;
using SysScrub.Core.Machine;
using SysScrub.Core.Settings;

namespace SysScrub.App.ViewModels;

/// <summary>Ayarlardaki dil düğmesi.</summary>
public sealed record LanguageChoice(string Culture, string Name, string CoverageLabel, bool IsSelected)
{
    public bool ShowCoverage => CoverageLabel.Length > 0;
}

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
    private readonly LocalizationService _localization;
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
        LocalizationService localization,
        QuarantineStore quarantine,
        ScheduledMaintenance maintenance,
        SystemInfoService systemInfo,
        AppUpdateViewModel update,
        ILogger<SettingsViewModel> logger)
    {
        _store = store;
        _theme = theme;
        _localization = localization;
        _quarantine = quarantine;
        _maintenance = maintenance;
        _systemInfo = systemInfo;
        _logger = logger;

        Update = update;

        // Dil değişince tüm metinler yeniden okunmalı; boş ad her bağlamayı tazeliyor.
        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);

        IsElevated = systemInfo.Capture().IsElevated;

        BuildLanguages();
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

    public string RetentionLabel => T("Set_Days", RetentionDays);

    public string QuarantineSummary => QuarantineRuns == 0
        ? T("Set_Quarantine_Empty")
        : T("Set_Quarantine_Summary", QuarantineRuns, ByteSize.Format(QuarantineBytes));

    public bool HasQuarantine => QuarantineRuns > 0;

    public string QuarantinePath => _quarantine.RootDirectory;

    [RelayCommand]
    private void PurgeQuarantine()
    {
        int removed = _quarantine.Purge(_store.Current.QuarantineRetention);

        StatusMessage = removed == 0
            ? T("Set_PurgeNone", RetentionLabel)
            : T("Set_PurgeDone", removed);

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
                    ? T("Set_ScheduleOn", $"{ScheduledHour:00}")
                    : T("Set_ScheduleOff"));

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

    public string ScheduleLabel => T("Set_Hour_Label", $"{ScheduledHour:00}");

    public string MaintenanceDetail
    {
        get
        {
            if (!MaintenanceState.Exists)
            {
                return T("Set_Weekly_Off");
            }

            return MaintenanceState.NextRun is { } next
                ? T("Set_Weekly_Next", $"{next:dd MMMM yyyy HH:mm}")
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
                return T("Set_Weekly_NeedsAdmin");
            }

            return ScheduledMaintenance.CliPath is null
                ? T("Set_Weekly_NoCli")
                : string.Empty;
        }
    }

    public bool HasScheduleBlockedReason => ScheduleBlockedReason.Length > 0;

    // ------------------------------------------------------------------ veri ve gizlilik

    public string DataDirectory => AppPaths.DataDirectory;

    public string SettingsFilePath => _store.FilePath;

    public string SettingsFileLabel => T("Set_SettingsFile", SettingsFilePath);

    public bool IsPortable => AppPaths.IsPortable;

    public string StorageModeLabel => AppPaths.IsPortable
        ? T("Set_Portable")
        : T("Set_NotPortable");

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
            StatusMessage = T("Set_FolderFailed", path);
        }
    }

    // ------------------------------------------------------------------ güncelleme

    /// <summary>Uygulamanın kendi güncelleme kartı; ayrı görünüm modelinde yaşıyor.</summary>
    public AppUpdateViewModel Update { get; }

    // ------------------------------------------------------------------ hakkında

    public string VersionLabel =>
        typeof(SettingsViewModel).Assembly.GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : T("Common_Unknown");

    public string VersionText => T("Set_Version", VersionLabel);

    public string SystemLabel
    {
        get
        {
            SystemSnapshot snapshot = _systemInfo.Capture();

            string system = snapshot.WindowsBuild.Length > 0
                ? T("Set_SystemBuild", snapshot.OperatingSystem, snapshot.WindowsBuild)
                : snapshot.OperatingSystem;

            return T("Set_SystemLine", system, T(IsElevated ? "Set_Elevated" : "Set_NotElevated"));
        }
    }

    // ------------------------------------------------------------------ dil

    /// <summary>Kataloglar + "otomatik" seçeneği.</summary>
    public IReadOnlyList<LanguageChoice> Languages { get; private set; } = [];

    /// <summary>
    /// Seçili dilin çeviri durumu. Eksik çeviriyi gizlemiyoruz: kullanıcı bazı
    /// ekranların neden Türkçe kaldığını bilsin.
    /// </summary>
    public string CoverageNotice
    {
        get
        {
            LanguageOption? option = _localization.Find(_localization.Culture);

            return option is null || option.IsComplete
                ? string.Empty
                : _localization.Format("Set_Language_Coverage", option.NativeName, option.CoveragePercent);
        }
    }

    public bool HasCoverageNotice => CoverageNotice.Length > 0;

    [RelayCommand]
    private void SetLanguage(string culture)
    {
        _localization.Use(culture);
        _store.Update(s => s with { Language = culture });

        BuildLanguages();

        OnPropertyChanged(nameof(CoverageNotice));
        OnPropertyChanged(nameof(HasCoverageNotice));
        OnPropertyChanged(nameof(SystemLabel));
    }

    /// <summary>Turu tekrar açar; kullanıcı arayüz anlatımına dönebilsin.</summary>
    [RelayCommand]
    private void ShowTour()
    {
        _store.Update(s => s with { TourCompleted = false });

        App.ShowWelcome();

        BuildLanguages();
        Refresh();
    }

    private void BuildLanguages()
    {
        string selected = _store.Current.Language;

        var choices = new List<LanguageChoice>
        {
            new(AppSettings.AutomaticLanguage,
                _localization["Set_Language_Auto"],
                string.Empty,
                selected == AppSettings.AutomaticLanguage)
        };

        choices.AddRange(_localization.Languages.Select(option => new LanguageChoice(
            option.Culture,
            option.NativeName,
            option.IsComplete ? string.Empty : $"%{option.CoveragePercent}",
            selected == option.Culture)));

        Languages = choices;
        OnPropertyChanged(nameof(Languages));
    }

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
