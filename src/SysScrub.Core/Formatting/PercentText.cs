using System.Globalization;

namespace SysScrub.Core.Formatting;

/// <summary>
/// Yüzde gösterimi.
///
/// İşaretin yeri dile göre değişiyor: Türkçe'de sayının önünde (%97), İngilizce
/// ve Çince'de arkasında (97%), Almanca'da arkasında ve boşluklu (97 %). Elle
/// "%{0}" yazmak arayüzü İngilizce'ye çevirince yanlış görünüyordu; .NET'in
/// kültür kuralları bunu zaten biliyor.
/// </summary>
public static class PercentText
{
    /// <summary>0–100 arası tam sayıyı kültürün yüzde biçimine çevirir.</summary>
    public static string Format(int percent) =>
        (percent / 100d).ToString("P0", CultureInfo.CurrentCulture);

    /// <summary>0–1 arası oranı kültürün yüzde biçimine çevirir.</summary>
    public static string FromFraction(double fraction) =>
        fraction.ToString("P0", CultureInfo.CurrentCulture);
}
