using SysScrub.Core.Cleaning;
using SysScrub.Core.Formatting;
using SysScrub.Core.Machine;
using SysScrub.Core.Rules;
using SysScrub.Core.Safety;

namespace SysScrub.Cli.Commands;

/// <summary>
/// Temizlik. Varsayılan davranış kuru çalıştırma: ne olacağını gösterir, hiçbir şey silmez.
/// Gerçekten silmek için --apply gerekiyor — yanlışlıkla veri silinmesin.
/// </summary>
internal static class CleanCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        bool apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
        bool includeAll = args.Contains("--all", StringComparer.OrdinalIgnoreCase);
        bool assumeYes = args.Contains("--yes", StringComparer.OrdinalIgnoreCase);

        var resolver = new PathResolver();
        var guard = new SafetyGuard(resolver);
        var quarantine = new QuarantineStore();
        var history = new HistoryStore();
        var systemInfo = new SystemInfoService();

        var scanner = new ScanEngine(resolver, guard);
        var cleaner = new CleanEngine(guard, quarantine, history, systemInfo);

        RuleSet ruleSet = new RuleLoader().Load();
        bool elevated = systemInfo.Capture().IsElevated;

        var scanOptions = new ScanOptions
        {
            IsElevated = elevated,
            EnabledRuleIds = includeAll
                ? ruleSet.Rules.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : null
        };

        Console.WriteLine("Taranıyor...");
        ScanReport report = await scanner.ScanAsync(ruleSet, scanOptions);

        IReadOnlyList<RuleScanResult> findings = report.WithFindings;

        if (findings.Count == 0)
        {
            Console.WriteLine("Temizlenecek bir şey bulunamadı.");
            return 0;
        }

        Console.WriteLine();

        foreach (RuleScanResult result in findings.OrderByDescending(r => r.Bytes))
        {
            Console.WriteLine(
                $"  {ByteSize.Format(result.Bytes),10}  {result.Count,7} dosya   {result.Rule.Name.Resolve()}");
        }

        Console.WriteLine();
        Console.WriteLine($"Toplam: {ByteSize.Format(report.TotalBytes)} / {report.TotalCount} dosya");
        Console.WriteLine();

        if (!apply)
        {
            Console.WriteLine("Kuru çalıştırma — hiçbir dosya silinmedi.");
            Console.WriteLine("Gerçekten temizlemek için: sysscrub-cli clean --apply");
            return 0;
        }

        if (!assumeYes && !Confirm(report))
        {
            Console.WriteLine("İptal edildi.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("Temizleniyor...");

        CleanResult cleanResult = await cleaner.CleanAsync(findings, new CleanOptions());

        PrintResult(cleanResult, quarantine);
        return cleanResult.Failures.Count > 0 ? 2 : 0;
    }

    private static bool Confirm(ScanReport report)
    {
        Console.Write($"{ByteSize.Format(report.TotalBytes)} temizlenecek. Devam edilsin mi? (e/H) ");
        string? answer = Console.ReadLine();

        return answer is not null && answer.Trim().StartsWith('e');
    }

    private static void PrintResult(CleanResult result, QuarantineStore quarantine)
    {
        Console.WriteLine();
        Console.WriteLine($"Silinen        : {result.Deleted}");

        if (result.Quarantined > 0)
        {
            Console.WriteLine($"Karantinaya    : {result.Quarantined}  ({quarantine.RootDirectory})");
        }

        if (result.SentToRecycleBin > 0)
        {
            Console.WriteLine($"Geri dönüşüme  : {result.SentToRecycleBin}");
        }

        if (result.ScheduledForReboot > 0)
        {
            Console.WriteLine($"Yeniden başlatmada silinecek: {result.ScheduledForReboot}");
        }

        if (result.SkippedByGuard > 0)
        {
            Console.WriteLine($"Güvenlik denetimi atladı: {result.SkippedByGuard}");
        }

        Console.WriteLine($"Kazanılan      : {ByteSize.Format(result.BytesFreed)}");
        Console.WriteLine($"Diskte ölçülen : {ByteSize.Format(Math.Max(0, result.MeasuredGain))}");
        Console.WriteLine($"Süre           : {result.Duration.TotalSeconds:F1} sn");

        if (result.Failures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Silinemeyen {result.Failures.Count} dosya:");

            foreach (CleanFailure failure in result.Failures.Take(10))
            {
                Console.WriteLine($"  {failure.Path}");
                Console.WriteLine($"      {failure.Reason}");
            }

            if (result.Failures.Count > 10)
            {
                Console.WriteLine($"  ... ve {result.Failures.Count - 10} tane daha");
            }
        }

        if (result.IsReversible)
        {
            Console.WriteLine();
            Console.WriteLine($"Bu temizlik geri alınabilir:  sysscrub-cli undo {result.RunId}");
        }
    }
}
