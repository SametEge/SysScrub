using System.Windows;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SysScrub.App.Services;

public enum AppThemeMode
{
    /// <summary>Windows'un açık/koyu ayarını takip eder.</summary>
    System,
    Light,
    Dark
}

/// <summary>
/// Tema yönetimi. Kendi token sözlüğümüzü (Tokens.Dark/Light.xaml) WPF-UI'nin sözlüğüyle
/// birlikte değiştirir; ikisi aynı anahtarları taşıdığı için tek geçişte tutarlı kalırlar.
/// </summary>
public sealed class ThemeService
{
    private const string WpfUiDarkTheme = "pack://application:,,,/Wpf.Ui;component/Resources/Theme/Dark.xaml";
    private const string WpfUiLightTheme = "pack://application:,,,/Wpf.Ui;component/Resources/Theme/Light.xaml";
    private const string TokensDark = "Themes/Tokens.Dark.xaml";
    private const string TokensLight = "Themes/Tokens.Light.xaml";

    /// <summary>Token sözlüğünü tanımaktaki işaret anahtarı.</summary>
    private const string TokenMarkerKey = "SsGround";

    private AppThemeMode _mode = AppThemeMode.System;

    public event EventHandler<bool>? EffectiveThemeChanged;

    public AppThemeMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;
            Apply();
        }
    }

    public bool IsDark { get; private set; } = true;

    public void Initialize()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Apply();
    }

    public void Shutdown() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    /// <summary>Pencereye arkaplan malzemesini uygular. Pencere gösterildikten sonra çağrılmalı.</summary>
    public void ApplyBackdrop(Window window)
    {
        WindowBackgroundManager.UpdateBackground(
            window,
            IsDark ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_mode == AppThemeMode.System && e.Category == UserPreferenceCategory.General)
        {
            Apply();
        }
    }

    private void Apply()
    {
        bool dark = _mode switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => ApplicationThemeManager.GetSystemTheme() is not (SystemTheme.Light or SystemTheme.HCWhite)
        };

        SwapDictionary(WpfUiDarkTheme, WpfUiLightTheme, dark, isPackUri: true);
        SwapDictionary(TokensDark, TokensLight, dark, isPackUri: false);

        // WPF-UI'nin kendi durumunu da güncelle: bazı kontrolleri buna bakıyor.
        ApplicationThemeManager.Apply(
            dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica,
            updateAccent: false);

        // Vurgu rengimiz sistemden değil bizden gelir — WPF-UI'nin sistem vurgusunu ezmesini engelle.
        ReassertTokens(dark);

        if (IsDark != dark)
        {
            IsDark = dark;
            EffectiveThemeChanged?.Invoke(this, dark);
        }
        else
        {
            IsDark = dark;
        }

        foreach (Window window in Application.Current.Windows)
        {
            ApplyBackdrop(window);
        }
    }

    private static void SwapDictionary(string darkSource, string lightSource, bool dark, bool isPackUri)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        string wanted = dark ? darkSource : lightSource;
        string unwanted = dark ? lightSource : darkSource;

        for (int i = 0; i < merged.Count; i++)
        {
            string? source = merged[i].Source?.ToString();

            if (source is null || !source.EndsWith(TrimSource(unwanted), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            merged[i] = new ResourceDictionary
            {
                Source = isPackUri
                    ? new Uri(wanted, UriKind.Absolute)
                    : new Uri(wanted, UriKind.Relative)
            };

            return;
        }

        static string TrimSource(string s) => s[(s.LastIndexOf('/') + 1)..];
    }

    /// <summary>
    /// ApplicationThemeManager.Apply kendi sözlüğünü listenin sonuna ekleyebiliyor; bu durumda
    /// bizim token'larımız ezilir. Token sözlüğünü tekrar en sona taşıyarak son sözü bize bırakıyoruz.
    /// </summary>
    private static void ReassertTokens(bool dark)
    {
        var merged = Application.Current.Resources.MergedDictionaries;

        for (int i = 0; i < merged.Count; i++)
        {
            if (!merged[i].Contains(TokenMarkerKey))
            {
                continue;
            }

            if (i == merged.Count - 1)
            {
                return;
            }

            var tokens = merged[i];
            merged.RemoveAt(i);
            merged.Add(tokens);
            return;
        }

        // Hiç bulunamadıysa (beklenmiyor) yeniden ekle ki uygulama renksiz kalmasın.
        merged.Add(new ResourceDictionary
        {
            Source = new Uri(dark ? TokensDark : TokensLight, UriKind.Relative)
        });
    }
}
