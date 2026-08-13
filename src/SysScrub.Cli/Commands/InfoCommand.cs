using SysScrub.Core.Formatting;
using SysScrub.Core.Machine;

namespace SysScrub.Cli.Commands;

internal static class InfoCommand
{
    public static int Run()
    {
        SystemSnapshot snapshot = new SystemInfoService().Capture();

        Console.WriteLine(snapshot.WindowsBuild.Length > 0
            ? $"{snapshot.OperatingSystem} (derleme {snapshot.WindowsBuild})"
            : snapshot.OperatingSystem);
        Console.WriteLine($"Makine       : {snapshot.MachineName}");
        Console.WriteLine($"Açık kalma   : {DurationText.Humanize(snapshot.Uptime)}");
        Console.WriteLine($"Yetki        : {(snapshot.IsElevated ? "yönetici" : "sınırlı")}");
        Console.WriteLine($"Bellek       : {ByteSize.Format(snapshot.Memory.UsedBytes)} / {ByteSize.Format(snapshot.Memory.TotalBytes)}");
        Console.WriteLine($"Veri klasörü : {AppPaths.DataDirectory}{(AppPaths.IsPortable ? "  (taşınabilir mod)" : string.Empty)}");
        Console.WriteLine();
        Console.WriteLine("Diskler:");

        foreach (DriveSnapshot drive in snapshot.Drives)
        {
            string warning = drive.IsCriticallyFull ? "   <- %90 üzeri dolu" : string.Empty;

            Console.WriteLine(
                $"  {drive.Name,-4} {drive.Label,-20} " +
                $"{ByteSize.Format(drive.FreeBytes),10} boş / {ByteSize.Format(drive.TotalBytes),10}{warning}");
        }

        return 0;
    }
}
