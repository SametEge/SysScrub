using System.ComponentModel;
using System.Diagnostics;

namespace SysScrub.Core.Tests;

/// <summary>
/// Klasör bağlantısı (junction) oluşturur. Sembolik bağlantı yönetici hakkı isterken
/// junction istemiyor, ama grup ilkesi ya da dosya sistemi yine de engelleyebilir —
/// bu yüzden oluşturulamadığında testler sessizce geçilir, yanlış bir iddiada bulunulmaz.
/// </summary>
internal static class TestJunction
{
    public static bool TryCreate(string link, string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(10_000);
            return process.ExitCode == 0 && Directory.Exists(link);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Özyinelemeli silme bağlantı noktalarının içine girmeye çalışıp hata veriyor.
    /// Bağlantılar önce tek tek kaldırılır — hedefleri etkilenmez.
    /// </summary>
    public static void DeleteTree(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
            {
                Directory.Delete(directory, recursive: false);
            }
        }

        Directory.Delete(root, recursive: true);
    }
}
