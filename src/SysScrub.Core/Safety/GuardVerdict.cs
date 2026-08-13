using SysScrub.Core.Formatting;
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
        GuardDenialReason.None => CoreText.Get("Gv_Allowed", "İzin verildi."),
        GuardDenialReason.InvalidPath => CoreText.Get("Gv_BadPath", "Yol geçersiz ya da çözümlenemedi."),
        GuardDenialReason.NonLocalPath => CoreText.Get("Gv_Remote", "Ağ veya aygıt yolu; yerel temizlik kapsamı dışında."),
        GuardDenialReason.OutsideAllowedRoot => CoreText.Get("Gv_OutsideRoot", "Kuralın izin verilen klasörünün dışında."),
        GuardDenialReason.ProtectedSystemDirectory => CoreText.Get("Gv_Protected", "Korumalı Windows bileşeni."),
        GuardDenialReason.UserContent => CoreText.Get("Gv_UserDocument", "Kullanıcı belgesi; temizlik kapsamı dışında."),
        GuardDenialReason.ReparsePoint => CoreText.Get("Gv_Reparse", "Bağlantı noktası (junction/symlink); hedefteki veriyi silmemek için atlandı."),
        GuardDenialReason.CloudPlaceholder => CoreText.Get("Gv_CloudPlaceholder", "Bulut yer tutucusu; silinirse buluttaki dosya da gider."),
        GuardDenialReason.ApplicationOwnData => CoreText.Get("Gv_OwnFiles", "SysScrub'ın kendi dosyaları."),
        _ => CoreText.Get("Gv_UnknownReason", "Bilinmeyen sebeple reddedildi.")
    };
}
