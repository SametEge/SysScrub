using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysScrub.App.Localization;
using SysScrub.Core.Machine;
using SysScrub.Core.Settings;
using SysScrub.Core.Formatting;

namespace SysScrub.App.ViewModels;

/// <summary>Tur ekranındaki dil düğmesi.</summary>
public sealed partial class LanguageChoiceViewModel(LanguageOption option, bool isDetected) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public LanguageOption Option { get; } = option;

    public string Culture => Option.Culture;

    public string NativeName => Option.NativeName;

    /// <summary>Bilgisayarın dilinden saptanan seçenek; kullanıcı hangisinin neden seçili olduğunu görsün.</summary>
    public bool IsDetected { get; } = isDetected;

    public string CoverageLabel =>
        Option.IsComplete ? string.Empty : PercentText.Format(Option.CoveragePercent);

    public bool ShowCoverage => !Option.IsComplete;
}

/// <summary>Turdaki modül satırı.</summary>
public sealed record ModuleIntro(string IconKey, LocText Title, LocText Body)
{
    public ModuleIntro(string iconKey, string titleKey, string bodyKey)
        : this(iconKey, new LocText(titleKey), new LocText(bodyKey))
    {
    }
}

/// <summary>Güvenlik adımındaki madde.</summary>
public sealed record SafetyPoint(LocText Title, LocText Body)
{
    public SafetyPoint(string titleKey, string bodyKey)
        : this(new LocText(titleKey), new LocText(bodyKey))
    {
    }
}

/// <summary>
/// İlk açılış turu.
///
/// Amacı arayüzü anlatmak: kullanıcı ilk kez açtığında solda ne olduğunu, neyin
/// geri alınabileceğini ve hiçbir verinin dışarı çıkmadığını öğreniyor.
///
/// Dil ilk adımda soruluyor ve işletim sisteminin dilinden önceden seçiliyor —
/// kullanıcı anlamadığı bir dilde "İleri" aramak zorunda kalmasın.
/// </summary>
public sealed partial class WelcomeViewModel : ObservableObject
{
    /// <summary>Turdaki adım sayısı.</summary>
    public const int StepCount = 4;

    private readonly SettingsStore _settings;
    private readonly LocalizationService _localization;

    [ObservableProperty]
    private int _step;

    public WelcomeViewModel(SettingsStore settings, LocalizationService localization, SystemInfoService systemInfo)
    {
        _settings = settings;
        _localization = localization;

        IsElevated = systemInfo.Capture().IsElevated;

        string detected = localization.DetectSystemCulture();

        Languages = new ObservableCollection<LanguageChoiceViewModel>(
            localization.Languages.Select(l => new LanguageChoiceViewModel(l, l.Culture == detected)));

        Select(Languages.FirstOrDefault(l => l.Culture == localization.Culture)
               ?? Languages.FirstOrDefault(l => l.Culture == detected)
               ?? Languages.FirstOrDefault());
    }

    public ObservableCollection<LanguageChoiceViewModel> Languages { get; }

    public bool IsElevated { get; }

    /// <summary>Yönetici değilken ne kaçırıldığını söyleyen not son adımda çıkıyor.</summary>
    public bool ShowAdminHint => !IsElevated;

    /// <summary>Kapatıldığında turun bir daha gösterilmemesi için pencere bunu okuyor.</summary>
    public bool Completed { get; private set; }

    /// <summary>Modül tanıtımları: solda ne olduğunu anlatan asıl bölüm.</summary>
    public IReadOnlyList<ModuleIntro> Modules { get; } =
    [
        new("IconCleaner", "Nav_Cleaner", "Mod_Cleaner"),
        new("IconRegistry", "Nav_Registry", "Mod_Registry"),
        new("IconDrivers", "Nav_Drivers", "Mod_Drivers"),
        new("IconUpdates", "Nav_Updates", "Mod_Updates"),
        new("IconStartup", "Nav_Startup", "Mod_Startup"),
        new("IconPrograms", "Nav_Programs", "Mod_Programs"),
        new("IconDiskHealth", "Nav_DiskHealth", "Mod_DiskHealth"),
        new("IconDiskAnalysis", "Nav_DiskAnalysis", "Mod_DiskAnalysis"),
        new("IconTimeline", "Nav_Timeline", "Mod_Timeline")
    ];

    public IReadOnlyList<SafetyPoint> SafetyPoints { get; } =
    [
        new("Ob_Safety_1_Title", "Ob_Safety_1_Body"),
        new("Ob_Safety_2_Title", "Ob_Safety_2_Body"),
        new("Ob_Safety_3_Title", "Ob_Safety_3_Body")
    ];

    public IReadOnlyList<LocText> PrivacyPoints { get; } =
    [
        new("Ob_Privacy_1"), new("Ob_Privacy_2"), new("Ob_Privacy_3"), new("Ob_Privacy_4")
    ];

    // ------------------------------------------------------------------ adımlar

    public bool IsLanguageStep => Step == 0;

    public bool IsModulesStep => Step == 1;

    public bool IsSafetyStep => Step == 2;

    public bool IsPrivacyStep => Step == 3;

    public bool CanGoBack => Step > 0;

    public bool IsLastStep => Step == StepCount - 1;

    public string StepLabel => _localization.Format("Common_Step", Step + 1, StepCount);

    public string DetectedLabel => _localization.Format("Ob_Lang_Detected", _localization.SystemCultureName);

    public string CoverageLabel
    {
        get
        {
            LanguageOption? option = _localization.Find(_localization.Culture);

            return option is null || option.IsComplete
                ? string.Empty
                : _localization.Format("Ob_Lang_Coverage", option.CoveragePercent);
        }
    }

    public bool ShowCoverage => CoverageLabel.Length > 0;

    [RelayCommand]
    private void Next()
    {
        if (Step < StepCount - 1)
        {
            Step++;
        }
        else
        {
            Complete();
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (Step > 0)
        {
            Step--;
        }
    }

    [RelayCommand]
    private void Skip() => Complete();

    [RelayCommand]
    private void ChooseLanguage(LanguageChoiceViewModel choice)
    {
        Select(choice);

        _localization.Use(choice.Culture);
        _settings.Update(s => s with { Language = choice.Culture });

        RaiseLocalizedLabels();
    }

    private void Select(LanguageChoiceViewModel? choice)
    {
        foreach (LanguageChoiceViewModel language in Languages)
        {
            language.IsSelected = ReferenceEquals(language, choice);
        }
    }

    private void Complete()
    {
        // Tur tamamlandı işareti ayarlarda: bir daha açılışta çıkmasın.
        _settings.Update(s => s with { TourCompleted = true });

        Completed = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? CloseRequested;

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsLanguageStep));
        OnPropertyChanged(nameof(IsModulesStep));
        OnPropertyChanged(nameof(IsSafetyStep));
        OnPropertyChanged(nameof(IsPrivacyStep));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(StepLabel));
    }

    private void RaiseLocalizedLabels()
    {
        OnPropertyChanged(nameof(StepLabel));
        OnPropertyChanged(nameof(DetectedLabel));
        OnPropertyChanged(nameof(CoverageLabel));
        OnPropertyChanged(nameof(ShowCoverage));
    }
}
