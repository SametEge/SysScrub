using SysScrub.Core.Cleaning;
using SysScrub.Core.Formatting;
using SysScrub.Core.Machine;
using SysScrub.Core.Rules;
using SysScrub.Core.Safety;

namespace SysScrub.Cli.Commands;

/// <summary>
/// Salt-okunur tarama. Hiçbir şey silmez — çıktısı, temizlenmesi durumunda
/// ne kazanılacağının raporudur.
/// </summary>
internal static class ScanCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        bool includeAll = args.Contains("--all", StringComparer.OrdinalIgnoreCase);
        bool verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);

        var resolver = new PathResolver();
        var guard = new SafetyGuard(resolver);
        var engine = new ScanEngine(resolver, guard);

        RuleSet ruleSet = new RuleLoader().Load();
        bool elevated = new SystemInfoService().Capture().IsElevated;

        var options = new ScanOptions
        {
            IsElevated = elevated,
            EnabledRuleIds = includeAll
                ? ruleSet.Rules.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : null
        };

        Console.WriteLine($"{ruleSet.Rules.Count} kural yüklendi. Taranıyor...");

        if (!elevated)
        {
            Console.WriteLine("Not: yönetici hakkı yok, sistem klasörlerini gerektiren kurallar atlanıyor.");
        }

        Console.WriteLine();

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        ScanReport report = await engine.ScanAsync(ruleSet, options, null, cancellation.Token);

        PrintReport(report, verbose);
        return 0;
    }

    private static void PrintReport(ScanReport report, bool verbose)
    {
        IReadOnlyList<RuleScanResult> findings = report.WithFindings;

        if (findings.Count == 0)
        {
            Console.WriteLine("Temizlenecek bir şey bulunamadı.");
            return;
        }

        foreach (IGrouping<RuleCategory, RuleScanResult> category in findings
                     .GroupBy(r => r.Rule.Category)
                     .OrderBy(g => (int)g.Key))
        {
            Console.WriteLine(category.Key.ToString().ToUpperInvariant());

            foreach (RuleScanResult result in category.OrderByDescending(r => r.Bytes))
            {
                string blocked = result.RunningBlockers.Count > 0
                    ? $"   (açık: {string.Join(", ", result.RunningBlockers)})"
                    : string.Empty;

                Console.WriteLine(
                    $"  {ByteSize.Format(result.Bytes),10}  {result.Count,7} dosya   " +
                    $"{result.Rule.Name.Resolve()}{blocked}");

                if (verbose)
                {
                    foreach (ScanItem item in result.Items.OrderByDescending(i => i.Bytes).Take(10))
                    {
                        Console.WriteLine($"              {ByteSize.Format(item.Bytes),10}  {item.Path}");
                    }

                    if (result.Count > 10)
                    {
                        Console.WriteLine($"              ... ve {result.Count - 10} dosya daha");
                    }
                }
            }

            Console.WriteLine();
        }

        Console.WriteLine(new string('-', 60));
        Console.WriteLine(
            $"Toplam {ByteSize.Format(report.TotalBytes)} / {report.TotalCount} dosya   " +
            $"({report.Duration.TotalSeconds:F1} sn)");

        if (report.SkippedForElevation > 0)
        {
            Console.WriteLine($"{report.SkippedForElevation} kural yönetici hakkı olmadığı için atlandı.");
        }

        Console.WriteLine();
        Console.WriteLine("Bu bir rapordur; hiçbir dosya silinmedi.");
    }
}
