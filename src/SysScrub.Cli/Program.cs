using SysScrub.Core.Formatting;
using SysScrub.Core.System;

namespace SysScrub.Cli;

/// <summary>
/// Komut satırı arayüzü. Zamanlanmış/sessiz temizlik ve teknisyen raporu buradan çalışır.
/// Faz 0'da yalnızca sistem özetini yazdırır; temizlik komutları Faz 1'de eklenecek.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "info" => PrintSystemInfo(),
            "version" => PrintVersion(),
            _ => UnknownCommand(args[0])
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine("SysScrub komut satırı");
        Console.WriteLine();
        Console.WriteLine("Kullanım: sysscrub <komut>");
        Console.WriteLine();
        Console.WriteLine("Komutlar:");
        Console.WriteLine("  info       Sistem özetini yazdırır");
        Console.WriteLine("  version    Sürüm bilgisini yazdırır");
        Console.WriteLine();
        Console.WriteLine("Temizlik komutları (clean, scan, report) Faz 1 ile geliyor.");
    }

    private static int PrintSystemInfo()
    {
        SystemSnapshot snapshot = new SystemInfoService().Capture();

        Console.WriteLine(snapshot.OperatingSystem);
        Console.WriteLine($"Makine       : {snapshot.MachineName}");
        Console.WriteLine($"Açık kalma   : {DurationText.Humanize(snapshot.Uptime)}");
        Console.WriteLine($"Yetki        : {(snapshot.IsElevated ? "yönetici" : "sınırlı")}");
        Console.WriteLine($"Bellek       : {ByteSize.Format(snapshot.Memory.UsedBytes)} / {ByteSize.Format(snapshot.Memory.TotalBytes)}");
        Console.WriteLine($"Veri klasörü : {AppPaths.DataDirectory}{(AppPaths.IsPortable ? "  (taşınabilir mod)" : string.Empty)}");
        Console.WriteLine();
        Console.WriteLine("Diskler:");

        foreach (DriveSnapshot drive in snapshot.Drives)
        {
            string warning = drive.IsCriticallyFull ? "   ← %90 üzeri dolu" : string.Empty;

            Console.WriteLine(
                $"  {drive.Name,-4} {drive.Label,-20} " +
                $"{ByteSize.Format(drive.FreeBytes),10} boş / {ByteSize.Format(drive.TotalBytes),10}{warning}");
        }

        return 0;
    }

    private static int PrintVersion()
    {
        var version = typeof(Program).Assembly.GetName().Version;
        Console.WriteLine($"SysScrub {version?.ToString(3) ?? "bilinmiyor"}");
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Bilinmeyen komut: {command}");
        Console.Error.WriteLine("Komut listesi için: sysscrub --help");
        return 1;
    }
}
