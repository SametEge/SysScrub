using SysScrub.Core.Disks;
using SysScrub.Core.Formatting;

namespace SysScrub.Cli.Commands;

/// <summary>
/// Disk sağlığı. Salt-okunur; diske yalnızca sorgu gönderilir.
/// S.M.A.R.T. okumak yönetici hakkı istiyor — olmadığında sebebi yazılır.
/// </summary>
internal static class DiskCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        bool verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);

        Console.WriteLine("Diskler okunuyor...");
        Console.WriteLine();

        DiskHealthReport report = await new DiskInventory().LoadAsync();

        if (report.Disks.Count == 0)
        {
            Console.Error.WriteLine("Hiçbir fiziksel disk okunamadı.");
            return 1;
        }

        foreach (DiskInfo disk in report.Disks)
        {
            Print(disk, verbose);
            Console.WriteLine();
        }

        Console.WriteLine(new string('-', 60));
        Console.WriteLine(
            $"{report.Disks.Count} disk · {report.ReadableCount} tanesinde S.M.A.R.T. okundu · " +
            $"{DurationText.FromMilliseconds((int)report.Duration.TotalMilliseconds)}");

        if (!report.IsElevated)
        {
            Console.WriteLine();
            Console.WriteLine("S.M.A.R.T. verisi için yönetici olarak çalıştırın.");
        }

        return 0;
    }

    private static void Print(DiskInfo disk, bool verbose)
    {
        Console.WriteLine($"DISK {disk.Index}  {disk.Model}");
        Console.WriteLine(
            $"  {disk.CapacityLabel} · {disk.BusType} · {(disk.IsSolidState ? "SSD" : "HDD")}" +
            (disk.FirmwareRevision is { } fw ? $" · bellenim {fw}" : string.Empty));

        Console.WriteLine($"  Durum: {Describe(disk.Status)}" +
                          (disk.HealthPercent is { } percent ? $"  (%{percent})" : string.Empty));
        Console.WriteLine($"  {disk.StatusReason}");

        if (disk.AccessMessage is { Length: > 0 } message)
        {
            Console.WriteLine($"  {message}");
        }

        if (disk.Nvme is { } nvme)
        {
            Console.WriteLine();
            Console.WriteLine($"  Sıcaklık            {nvme.TemperatureCelsius} °C");
            Console.WriteLine($"  Kalan yedek blok    %{nvme.AvailableSpare}  (eşik %{nvme.AvailableSpareThreshold})");
            Console.WriteLine($"  Tüketilen ömür      %{nvme.PercentageUsed}");
            Console.WriteLine($"  Yazılan veri        {ByteSize.Format(nvme.BytesWritten)}");
            Console.WriteLine($"  Okunan veri         {ByteSize.Format(nvme.BytesRead)}");
            Console.WriteLine($"  Açık kalma          {DurationText.Humanize(TimeSpan.FromHours(nvme.PowerOnHours))}");
            Console.WriteLine($"  Açılma sayısı       {nvme.PowerCycles:N0}");
            Console.WriteLine($"  Ani kapanma         {nvme.UnsafeShutdowns:N0}");
            Console.WriteLine($"  Veri hatası         {nvme.MediaErrors:N0}");

            if (verbose && nvme.SensorsCelsius.Count > 0)
            {
                Console.WriteLine($"  Sensörler           {string.Join(", ", nvme.SensorsCelsius.Select(s => $"{s} °C"))}");
            }
        }

        if (disk.Attributes.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  KIMLIK  GUNCEL  EN KOTU  ESIK  HAM            OZNITELIK");

            foreach (SmartAttribute attribute in disk.Attributes)
            {
                string flag = attribute.Status switch
                {
                    DiskHealthStatus.Bad => "!!",
                    DiskHealthStatus.Caution => " !",
                    _ => "  "
                };

                Console.WriteLine(
                    $"  {flag}0x{attribute.Id:X2}  {attribute.Current,6}  {attribute.Worst,7}  " +
                    $"{attribute.Threshold,4}  {attribute.RawHex}  {attribute.Name}");
            }
        }
    }

    private static string Describe(DiskHealthStatus status) => status switch
    {
        DiskHealthStatus.Good => "İYİ",
        DiskHealthStatus.Caution => "DİKKAT",
        DiskHealthStatus.Bad => "KÖTÜ",
        _ => "BİLİNMİYOR"
    };
}
