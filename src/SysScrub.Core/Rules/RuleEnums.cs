namespace SysScrub.Core.Rules;

/// <summary>
/// Bir kuralın risk seviyesi. Arayüzde doğrudan semantik renge bağlanır,
/// kullanıcı açıklamayı okumadan riski görebilsin.
/// </summary>
public enum RiskLevel
{
    /// <summary>
    /// Yeniden üretilebilir içerik. Silinmesi hiçbir veri kaybettirmez.
    /// İşlemin kendisi servis durdurmak gibi adımlar içerebilir; risk ölçüsü veri kaybıdır.
    /// </summary>
    Safe,

    /// <summary>Oturum, oturum açma bilgisi veya tercih kaybına yol açabilir.</summary>
    Caution,

    /// <summary>Ne yaptığını bilen kullanıcı için. Varsayılan olarak asla işaretli gelmez.</summary>
    Advanced
}

/// <summary>Silmenin nasıl yapılacağı.</summary>
public enum DeleteMode
{
    /// <summary>Karantinaya taşınır, saklama süresi dolunca gerçekten silinir. Varsayılan.</summary>
    Quarantine,

    /// <summary>Geri Dönüşüm Kutusu'na gönderilir; kullanıcı Windows üzerinden geri alabilir.</summary>
    RecycleBin,

    /// <summary>Doğrudan silinir. Yalnızca saf geçici dosyalar için — karantinada yer kaplamasınlar.</summary>
    Permanent
}

/// <summary>Arayüzdeki gruplama. Sıralama bu numaralandırmanın sırasıdır.</summary>
public enum RuleCategory
{
    Windows,
    Browsers,
    Applications,
    Gaming,
    Developer,
    Privacy,
    Other
}
