namespace SysScrub.App.Localization;

/// <summary>
/// Çeviri kısayolu: <c>using static SysScrub.App.Localization.L;</c> sonrası
/// görünüm modellerinde <c>T("Anahtar")</c> yazmak yeterli.
///
/// Adı bilerek çok kısa: bu çağrı görünüm modellerinde yüzlerce yerde geçiyor ve
/// uzun bir ad her cümlenin okunmasını zorlaştırıyor.
/// </summary>
public static class L
{
    public static string T(string key) => LocalizationService.Instance[key];

    public static string T(string key, params object?[] arguments) =>
        LocalizationService.Instance.Format(key, arguments);
}
