using SysScrub.Core.Cleaning;
using SysScrub.Core.Formatting;
using SysScrub.Core.Startup;

namespace SysScrub.Cli.Commands;

/// <summary>
/// Başlangıç öğeleri. Varsayılan olarak salt-okunur; değişiklik için
/// <c>--disable</c> ya da <c>--enable</c> ve öğe adı gerekir.
/// </summary>
internal static class StartupCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        string? disable = ValueAfter(args, "--disable");
        string? enable = ValueAfter(args, "--enable");

        var approvals = new StartupApprovedStore();
        var inventory = new StartupInventory(approvals, new BootPerformance());

        Console.WriteLine("Başlangıç öğeleri okunuyor...");
        Console.WriteLine();

        StartupInventoryReport report = await inventory.LoadAsync();

        if (disable is not null || enable is not null)
        {
            return await ToggleAsync(report, approvals, disable ?? enable!, enable is not null);
        }

        Print(report, args.Contains("--all", StringComparer.OrdinalIgnoreCase));

        return 0;
    }

    private static void Print(StartupInventoryReport report, bool includeDisabled)
    {
        StartupEntry[] enabled = report.Entries.Where(e => e.IsEnabled).ToArray();

        Console.WriteLine($"AÇILIŞTA ÇALIŞANLAR ({enabled.Length})");

        foreach (StartupEntry entry in enabled)
        {
            string delay = entry.BootDelayMs is { } ms ? DurationText.FromMilliseconds(ms) : string.Empty;
            string flag = entry.TargetMissing ? " [dosya yok]" : string.Empty;

            Console.WriteLine($"  {delay,8}  {entry.Name}{flag}");
            Console.WriteLine($"            └─ {entry.SourceLabel} · {Shorten(entry.Command)}");
        }

        if (includeDisabled)
        {
            StartupEntry[] disabled = report.Entries.Where(e => !e.IsEnabled).ToArray();

            Console.WriteLine();
            Console.WriteLine($"KAPALI ({disabled.Length})");

            foreach (StartupEntry entry in disabled)
            {
                Console.WriteLine($"            {entry.Name}  ({entry.SourceLabel})");
            }
        }

        Console.WriteLine();

        if (report.BrokenEntries.Count > 0)
        {
            Console.WriteLine(
                $"{report.BrokenEntries.Count} öğenin çalıştırdığı dosya yok; Windows her açılışta boşuna arıyor.");
        }

        Console.WriteLine(new string('-', 60));

        // Ölçüm yoksa sıfır yazmak yanıltıcı olur; sebebini söylüyoruz.
        string total = report.BootMeasurementsAvailable
            ? report.TotalDelayMs > 0
                ? $"ölçülen toplam gecikme {DurationText.FromMilliseconds(report.TotalDelayMs)}"
                : "Windows bu öğeler için henüz gecikme ölçmemiş"
            : "açılış ölçümü okunamadı (Tanılama-Performans günlüğü kapalı olabilir)";

        Console.WriteLine(
            $"Toplam {report.Entries.Count} öğe · {report.EnabledCount} açık · " +
            $"{report.DisabledCount} kapalı · {total}");
    }

    private static async Task<int> ToggleAsync(
        StartupInventoryReport report,
        StartupApprovedStore approvals,
        string name,
        bool enable)
    {
        StartupEntry[] matches = report.Entries
            .Where(e => e.Name.Contains(name, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
        {
            Console.Error.WriteLine($"Eşleşen başlangıç öğesi yok: {name}");
            return 1;
        }

        // Birden çok eşleşmede hangisinin kastedildiği belirsiz; yanlış öğeyi
        // kapatmaktansa listeleyip kullanıcıya bırakıyoruz.
        if (matches.Length > 1)
        {
            Console.Error.WriteLine($"'{name}' birden çok öğeyle eşleşti; adı daha belirgin yazın:");

            foreach (StartupEntry entry in matches)
            {
                Console.Error.WriteLine($"  {entry.Name}  ({entry.SourceLabel})");
            }

            return 1;
        }

        StartupEntry target = matches[0];
        var manager = new StartupManager(approvals, new HistoryStore());

        StartupChangeResult result = await manager.SetEnabledAsync(target, enable);

        if (!result.Success)
        {
            Console.Error.WriteLine(result.Message ?? "Değiştirilemedi.");
            return 1;
        }

        Console.WriteLine(
            enable
                ? $"{target.Name} açılışta yeniden çalışacak."
                : $"{target.Name} devre dışı bırakıldı. Kaydı silinmedi; --enable ile geri açabilirsiniz.");

        return 0;
    }

    private static string? ValueAfter(string[] args, string flag)
    {
        int index = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string Shorten(string command) =>
        command.Length <= 74 ? command : command[..71] + "...";
}
