using SysScrub.Core.Drivers;
using SysScrub.Core.Formatting;

namespace SysScrub.Cli.Commands;

/// <summary>Donanım envanteri. Salt-okunur; hiçbir sürücüye dokunmaz.</summary>
internal static class DriversCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        bool verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);
        bool problemsOnly = args.Contains("--problems", StringComparer.OrdinalIgnoreCase);
        bool checkUpdates = args.Contains("--updates", StringComparer.OrdinalIgnoreCase);
        bool backup = args.Contains("--backup", StringComparer.OrdinalIgnoreCase);

        if (checkUpdates)
        {
            return await CheckUpdatesAsync();
        }

        if (backup)
        {
            return await BackupAsync();
        }

        Console.WriteLine("Donanım envanteri okunuyor...");
        Console.WriteLine();

        DeviceInventoryReport report = await new DeviceInventory().LoadAsync();

        IReadOnlyList<DeviceInfo> problems = report.ProblemDevices;

        if (problems.Count > 0)
        {
            Console.WriteLine($"SORUNLU CİHAZLAR ({problems.Count})");

            foreach (DeviceInfo device in problems)
            {
                Console.WriteLine($"  {device.Name}");
                Console.WriteLine($"    └─ {device.ProblemDescription}");
            }

            Console.WriteLine();
        }

        if (problemsOnly)
        {
            if (problems.Count == 0)
            {
                Console.WriteLine("Sorunlu cihaz yok.");
            }

            return 0;
        }

        IReadOnlyList<DeviceInfo> aging = report.AgingDrivers;

        if (aging.Count > 0)
        {
            Console.WriteLine($"ESKİ SÜRÜCÜLER ({aging.Count})   iki yıldan eski, üretici sürücüsü");

            foreach (DeviceInfo device in aging.Take(15))
            {
                int years = (int)(device.DriverAge!.Value.TotalDays / 365);

                Console.WriteLine(
                    $"  {years,2} yıl   {device.DriverVersion,-18} {device.Name}");
            }

            Console.WriteLine();
        }

        if (verbose)
        {
            foreach (DeviceGroup group in report.GroupByClass())
            {
                Console.WriteLine($"{group.DisplayName.ToUpperInvariant()}  ({group.Devices.Count})");

                foreach (DeviceInfo device in group.Devices)
                {
                    string flag = device.HasProblem ? " [!]" : device.IsSigned ? string.Empty : " [imzasız]";
                    string date = device.DriverDate?.ToString("dd.MM.yyyy") ?? "—";

                    Console.WriteLine($"  {date}  {device.DriverVersion,-18} {device.Name}{flag}");
                }

                Console.WriteLine();
            }
        }

        Console.WriteLine(new string('-', 60));
        Console.WriteLine(
            $"Toplam {report.Devices.Count} cihaz · {problems.Count} sorunlu · " +
            $"{aging.Count} eski sürücü  ({report.Duration.TotalSeconds:F1} sn)");

        return 0;
    }

    private static async Task<int> CheckUpdatesAsync()
    {
        Console.WriteLine("Windows Update'te sürücü güncellemesi aranıyor...");
        Console.WriteLine("Bu arama çevrimiçi yapılır ve bir dakikaya kadar sürebilir.");
        Console.WriteLine();

        DriverSearchResult result = await new WindowsUpdateDriverSource().SearchAsync();

        Console.WriteLine(result.Describe());
        Console.WriteLine();

        foreach (DriverUpdate update in result.Updates)
        {
            Console.WriteLine($"  {update.Title}");

            if (update.Manufacturer is not null || update.Date is not null)
            {
                Console.WriteLine(
                    $"    └─ {update.Manufacturer ?? "üretici bilinmiyor"} · " +
                    $"{update.Date?.ToString("dd.MM.yyyy") ?? "tarih yok"} · " +
                    $"{ByteSize.Format(update.SizeBytes)}");
            }
        }

        if (result.Updates.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Kurulum arayüzden yapılır; öncesinde sürücü yedeği ve geri yükleme noktası alınır.");
        }

        Console.WriteLine();
        Console.WriteLine($"Süre: {result.Duration.TotalSeconds:F1} sn");

        return result.Outcome == DriverSearchOutcome.Completed ? 0 : 1;
    }

    private static async Task<int> BackupAsync()
    {
        Console.WriteLine("Üçüncü parti sürücüler yedekleniyor (pnputil)...");
        Console.WriteLine();

        DriverBackupResult result = await new DriverBackup().ExportAllAsync();

        Console.WriteLine(result.Describe());

        if (result.Path is not null)
        {
            Console.WriteLine($"Klasör: {result.Path}");
        }

        return result.Succeeded ? 0 : 1;
    }
}
