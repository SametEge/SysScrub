using SysScrub.Core.Cleaning;
using SysScrub.Core.Machine;
using SysScrub.Core.RegistryCleaning;

namespace SysScrub.Cli.Commands;

/// <summary>
/// Registry taraması ve temizliği. Temizlik varsayılan olarak kuru çalıştırma;
/// gerçekten silmek için --apply gerekiyor.
/// </summary>
internal static class RegistryCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        bool apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
        bool all = args.Contains("--all", StringComparer.OrdinalIgnoreCase);
        bool verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);

        var guard = new RegistryGuard();
        var scanner = new RegistryScanEngine(guard);
        var systemInfo = new SystemInfoService();
        bool elevated = systemInfo.Capture().IsElevated;

        var options = new RegistryScanOptions
        {
            IsElevated = elevated,
            EnabledScannerIds = all
                ? scanner.Scanners.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : null
        };

        Console.WriteLine($"{scanner.Scanners.Count} tarayıcı yüklendi. Registry taranıyor...");

        if (!elevated)
        {
            Console.WriteLine("Not: yönetici hakkı yok, makine geneli tarayıcılar atlanıyor.");
        }

        Console.WriteLine();

        RegistryScanReport report = await scanner.ScanAsync(options);
        IReadOnlyList<RegistryScannerResult> findings = report.WithFindings;

        if (findings.Count == 0)
        {
            Console.WriteLine("Ölü kayıt bulunamadı.");
            return 0;
        }

        foreach (RegistryScannerResult result in findings.OrderByDescending(r => r.Count))
        {
            Console.WriteLine($"  {result.Count,6} kayıt   {result.Scanner.Title}");

            if (verbose)
            {
                foreach (RegistryFinding finding in result.Findings.Take(8))
                {
                    Console.WriteLine($"           {finding.Location.DisplayPath}");
                    Console.WriteLine($"           └─ {finding.Reason}: {finding.Target}");
                }

                if (result.Count > 8)
                {
                    Console.WriteLine($"           ... ve {result.Count - 8} kayıt daha");
                }

                Console.WriteLine();
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Toplam {report.TotalCount} ölü kayıt ({report.Duration.TotalSeconds:F1} sn)");
        Console.WriteLine();

        if (!apply)
        {
            Console.WriteLine("Kuru çalıştırma — hiçbir kayıt silinmedi.");
            Console.WriteLine("Gerçekten temizlemek için: sysscrub-cli registry --apply");
            return 0;
        }

        var cleaner = new RegistryCleanEngine(guard, new HistoryStore(), new SystemRestorePoint());

        RegistryFinding[] all_findings = findings.SelectMany(f => f.Findings).ToArray();

        Console.Write($"{all_findings.Length} kayıt silinecek. Devam edilsin mi? (e/H) ");

        if (Console.ReadLine()?.Trim().StartsWith('e') != true)
        {
            Console.WriteLine("İptal edildi.");
            return 0;
        }

        RegistryCleanResult cleanResult = await cleaner.CleanAsync(all_findings, new RegistryCleanOptions());

        Console.WriteLine();
        Console.WriteLine($"Silinen        : {cleanResult.Removed}");

        if (cleanResult.SkippedByGuard > 0)
        {
            Console.WriteLine($"Guard atladı   : {cleanResult.SkippedByGuard}");
        }

        if (cleanResult.RestorePoint is { } restorePoint)
        {
            Console.WriteLine($"Geri yükleme   : {restorePoint.Describe()}");
        }

        if (cleanResult.BackupPath is not null)
        {
            Console.WriteLine($"Yedek          : {cleanResult.BackupPath}");
        }

        foreach (string failure in cleanResult.Failures.Take(10))
        {
            Console.Error.WriteLine($"  {failure}");
        }

        return cleanResult.Failures.Count > 0 ? 2 : 0;
    }
}
