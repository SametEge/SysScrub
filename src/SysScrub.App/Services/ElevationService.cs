using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using SysScrub.Core.Machine;

namespace SysScrub.App.Services;

/// <summary>
/// Yükseltilmiş hak yönetimi.
///
/// Uygulama manifestinde <c>requireAdministrator</c> var; exe çift tıklandığında
/// Windows UAC istemini kendisi gösterir ve uygulama yönetici olarak açılır.
/// Bu servis, o istemin atlandığı durumlar için: geliştirme sırasında
/// <c>dotnet SysScrub.dll</c> ile çalıştırma ya da manifesti olmayan bir başlatıcı.
/// </summary>
public sealed class ElevationService(ILogger<ElevationService> logger)
{
    private const string AppExecutableName = "SysScrub.exe";

    public bool IsElevated { get; } = new SystemInfoService().Capture().IsElevated;

    /// <summary>Yeniden başlatılacak exe bulunabiliyorsa düğme gösterilir.</summary>
    public bool CanRestart => FindExecutable() is not null;

    /// <summary>
    /// Uygulamayı yönetici olarak yeniden başlatır ve mevcut örneği kapatır.
    /// Kullanıcı UAC istemini reddederse hiçbir şey olmaz, uygulama açık kalır.
    /// </summary>
    public bool TryRestartElevated()
    {
        string? executable = FindExecutable();

        if (executable is null)
        {
            logger.LogWarning("Yeniden başlatılacak çalıştırılabilir dosya bulunamadı");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas"
            });

            Application.Current.Shutdown();
            return true;
        }
        catch (Win32Exception ex)
        {
            // 1223 = kullanıcı UAC istemini reddetti. Hata değil, tercih.
            if (ex.NativeErrorCode != 1223)
            {
                logger.LogWarning(ex, "Yönetici olarak yeniden başlatma başarısız");
            }

            return false;
        }
    }

    /// <summary>
    /// Tek dosya yayınında süreç yolu doğrudan exe'yi gösterir. Geliştirme sırasında
    /// süreç dotnet.exe olduğu için uygulamanın exe'si klasörden aranır.
    /// </summary>
    private static string? FindExecutable()
    {
        string? processPath = Environment.ProcessPath;

        if (processPath is not null &&
            Path.GetFileName(processPath).Equals(AppExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        string candidate = Path.Combine(AppContext.BaseDirectory, AppExecutableName);

        return File.Exists(candidate) ? candidate : null;
    }
}
