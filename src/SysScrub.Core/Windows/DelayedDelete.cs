using System.Runtime.InteropServices;

namespace SysScrub.Core.Windows;

/// <summary>
/// Kilitli dosyaları yeniden başlatmada silinmek üzere işaretler.
///
/// Açık bir tarayıcının önbellek dosyası silinemez. Alternatifler: kullanıcıdan
/// programı kapatmasını istemek (rahatsız edici) veya süreci zorla sonlandırmak (tehlikeli).
/// Windows'un kendi mekanizması ikisinden de iyi: dosya bir sonraki açılışta,
/// hiçbir şey çalışmıyorken silinir.
/// </summary>
public static class DelayedDelete
{
    private const uint MoveFileDelayUntilReboot = 0x00000004;

    /// <summary>
    /// Dosyayı yeniden başlatmada silinmek üzere kaydeder.
    /// Yönetici hakkı gerektirir; haklar yoksa false döner.
    /// </summary>
    public static bool ScheduleFileDeletion(string path)
    {
        try
        {
            // Hedefin null olması "sil" anlamına geliyor.
            return MoveFileEx(path, null, MoveFileDelayUntilReboot);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, uint flags);
}
