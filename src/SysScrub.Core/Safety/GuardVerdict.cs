namespace SysScrub.Core.Safety;

/// <summary>Bir yolun neden silinemeyeceği. Kullanıcıya gösterilen açıklamalar buradan üretilir.</summary>
public enum GuardDenialReason
{
    None = 0,

    /// <summary>Yol çözümlenemedi, boş ya da geçersiz karakter içeriyor.</summary>
    InvalidPath,

    /// <summary>Ağ paylaşımı veya aygıt yolu. Yerel bakım aracı bunlara dokunmaz.</summary>
    NonLocalPath,

    /// <summary>Kuralın izin verilen kökünün dışında. Kaçış denemesi de buraya düşer.</summary>
    OutsideAllowedRoot,

    /// <summary>System32, WinSxS, DriverStore gibi işletim sistemi bileşenleri.</summary>
    ProtectedSystemDirectory,

    /// <summary>Kullanıcının belgeleri, masaüstü, resimleri. Bir temizleyicinin işi değil.</summary>
    UserContent,

    /// <summary>Junction veya sembolik bağlantı. İzlenirse hedefteki veri silinir.</summary>
    ReparsePoint,

    /// <summary>OneDrive gibi bulut yer tutucusu. Silinirse buluttaki asıl dosya gider.</summary>
    CloudPlaceholder,

    /// <summary>SysScrub'ın kendi kurulum klasörü, günlükleri veya karantinası.</summary>
    ApplicationOwnData
}

/// <summary>Denetim sonucu. İzin verilmediğinde nedeni taşır.</summary>
public readonly record struct GuardVerdict(bool IsAllowed, GuardDenialReason Reason)
{
    public static GuardVerdict Allow { get; } = new(true, GuardDenialReason.None);

    public static GuardVerdict Deny(GuardDenialReason reason) => new(false, reason);

    /// <summary>Kullanıcıya gösterilecek tek cümlelik açıklama.</summary>
    public string Describe() => Reason switch
    {
        GuardDenialReason.None => "İzin verildi.",
        GuardDenialReason.InvalidPath => "Yol geçersiz ya da çözümlenemedi.",
        GuardDenialReason.NonLocalPath => "Ağ veya aygıt yolu; yerel temizlik kapsamı dışında.",
        GuardDenialReason.OutsideAllowedRoot => "Kuralın izin verilen klasörünün dışında.",
        GuardDenialReason.ProtectedSystemDirectory => "Korumalı Windows bileşeni.",
        GuardDenialReason.UserContent => "Kullanıcı belgesi; temizlik kapsamı dışında.",
        GuardDenialReason.ReparsePoint => "Bağlantı noktası (junction/symlink); hedefteki veriyi silmemek için atlandı.",
        GuardDenialReason.CloudPlaceholder => "Bulut yer tutucusu; silinirse buluttaki dosya da gider.",
        GuardDenialReason.ApplicationOwnData => "SysScrub'ın kendi dosyaları.",
        _ => "Bilinmeyen sebeple reddedildi."
    };
}
