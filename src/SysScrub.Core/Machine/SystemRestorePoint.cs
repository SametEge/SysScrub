using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SysScrub.Core.Formatting;

namespace SysScrub.Core.Machine;

/// <summary>Geri yükleme noktası oluşturma denemesinin sonucu.</summary>
public enum RestorePointOutcome
{
    Created,

    /// <summary>Sistem Koruması kapalı. Kullanıcıya söylenmeli, sessizce geçilmemeli.</summary>
    Disabled,

    /// <summary>Windows aynı gün içinde ikinci noktayı reddetti; zaten yeni bir nokta var.</summary>
    Throttled,

    /// <summary>Yönetici hakkı yok.</summary>
    NotElevated,

    Failed
}

public sealed record RestorePointResult(RestorePointOutcome Outcome, string? Message = null)
{
    /// <summary>
    /// İşleme devam etmek güvenli mi. Kısıtlama durumunda zaten yakın zamanlı bir
    /// nokta var demektir; devam edilebilir.
    /// </summary>
    public bool IsAcceptable => Outcome is RestorePointOutcome.Created or RestorePointOutcome.Throttled;

    public string Describe() => Outcome switch
    {
        RestorePointOutcome.Created => CoreText.Get("Sr_Created", "Sistem geri yükleme noktası oluşturuldu."),
        RestorePointOutcome.Throttled => CoreText.Get("Sr_Recent", "Yakın zamanda oluşturulmuş bir geri yükleme noktası zaten var."),
        RestorePointOutcome.Disabled => CoreText.Get("Sr_Disabled", "Sistem Koruması kapalı olduğu için geri yükleme noktası oluşturulamadı."),
        RestorePointOutcome.NotElevated => CoreText.Get("Sr_NeedsAdmin", "Geri yükleme noktası için yönetici hakkı gerekiyor."),
        _ => Message ?? CoreText.Get("Sr_Failed", "Geri yükleme noktası oluşturulamadı.")
    };
}

/// <summary>
/// Registry temizliği, sürücü kurulumu ve debloat öncesi sistem geri yükleme noktası oluşturur.
///
/// Kendi .reg yedeğimiz zaten var; bu ikinci güvenlik ağı. Bir registry silmesinin
/// beklenmedik bir yan etkisi olursa kullanıcının Windows'un kendi mekanizmasıyla
/// geri dönebilmesi gerekiyor.
/// </summary>
public sealed class SystemRestorePoint(ILogger<SystemRestorePoint>? logger = null)
{
    private const int ModifySettings = 12;
    private const int BeginSystemChange = 100;

    /// <summary>Windows varsayılan olarak 24 saatte bir noktaya izin veriyor.</summary>
    private const uint ErrorServiceDisabled = 0x422;

    public RestorePointResult TryCreate(string description)
    {
        try
        {
            using var managementClass = new ManagementClass(@"\\.\root\default:SystemRestore");
            using ManagementBaseObject parameters = managementClass.GetMethodParameters("CreateRestorePoint");

            parameters["Description"] = description;
            parameters["RestorePointType"] = ModifySettings;
            parameters["EventType"] = BeginSystemChange;

            using ManagementBaseObject result =
                managementClass.InvokeMethod("CreateRestorePoint", parameters, null);

            uint returnValue = Convert.ToUInt32(result["ReturnValue"]);

            if (returnValue == 0)
            {
                logger?.LogInformation("Geri yükleme noktası oluşturuldu: {Description}", description);
                return new RestorePointResult(RestorePointOutcome.Created);
            }

            if (returnValue == ErrorServiceDisabled)
            {
                return new RestorePointResult(RestorePointOutcome.Disabled);
            }

            // 1058 ve benzeri dönüşler genelde "bugün zaten nokta oluşturuldu" anlamına geliyor.
            logger?.LogWarning("Geri yükleme noktası dönüş kodu {Code}", returnValue);
            return new RestorePointResult(RestorePointOutcome.Throttled, $"Dönüş kodu {returnValue}");
        }
        catch (UnauthorizedAccessException)
        {
            return new RestorePointResult(RestorePointOutcome.NotElevated);
        }
        catch (ManagementException ex)
        {
            logger?.LogWarning(ex, "Geri yükleme noktası oluşturulamadı");
            return new RestorePointResult(RestorePointOutcome.Failed, ex.Message);
        }
        catch (COMException ex)
        {
            logger?.LogWarning(ex, "Geri yükleme noktası oluşturulamadı");
            return new RestorePointResult(RestorePointOutcome.Failed, ex.Message);
        }
    }
}
