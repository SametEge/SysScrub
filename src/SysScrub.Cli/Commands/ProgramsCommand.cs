using SysScrub.Core.Formatting;
using SysScrub.Core.Programs;

namespace SysScrub.Cli.Commands;

/// <summary>
/// Kurulu programlar. Salt-okunur; kaldırma arayüzden yapılıyor çünkü
/// kaldırıcıların çoğu pencere açıyor ve kullanıcı etkileşimi istiyor.
/// </summary>
internal static class ProgramsCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        bool includeComponents = args.Contains("--all", StringComparer.OrdinalIgnoreCase);
        bool measure = args.Contains("--size", StringComparer.OrdinalIgnoreCase);
        string? filter = ValueAfter(args, "--search");

        Console.WriteLine("Kurulu programlar okunuyor...");
        Console.WriteLine();

        ProgramInventoryReport report = await new ProgramInventory().LoadAsync();

        IReadOnlyList<InstalledProgram> programs = report.Programs;

        if (measure)
        {
            programs = await MeasureAsync(programs);
        }

        InstalledProgram[] shown = programs
            .Where(p => includeComponents || !p.IsSystemComponent)
            .Where(p => filter is null || p.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(p => p.SizeBytes)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        foreach (InstalledProgram program in shown)
        {
            string size = program.HasSize ? ByteSize.Format(program.SizeBytes) : "—";
            string date = program.InstallDate?.ToString("dd.MM.yyyy") ?? string.Empty;

            Console.WriteLine($"  {size,10}  {date,10}  {program.Name}");
            Console.WriteLine(
                $"              └─ {program.Publisher ?? "yayıncı bilinmiyor"} · " +
                $"{program.Version ?? "sürüm yok"} · {program.SourceLabel}" +
                (program.UninstallerMissing ? " · KALDIRICI DOSYASI KAYIP"
                    : program.CanUninstall ? string.Empty : " · KALDIRMA KOMUTU YOK"));
        }

        Console.WriteLine();
        Console.WriteLine(new string('-', 60));

        long total = shown.Sum(p => p.SizeBytes);

        int broken = shown.Count(p => p.UninstallerMissing);

        Console.WriteLine(
            $"{shown.Length} program · {report.StoreCount} Store paketi · " +
            $"{report.ComponentCount} gizli bileşen · " +
            (total > 0 ? $"bilinen toplam {ByteSize.Format(total)}" : "boyut ölçülmedi (--size ile ölçülür)"));

        if (broken > 0)
        {
            Console.WriteLine($"{broken} programın kaldırıcı dosyası kayıp; kaydı elle temizlenmeli.");
        }

        return 0;
    }

    /// <summary>Boyut ölçümü diski tarıyor; istenmedikçe yapılmıyor.</summary>
    private static async Task<IReadOnlyList<InstalledProgram>> MeasureAsync(IReadOnlyList<InstalledProgram> programs)
    {
        Console.WriteLine("Kurulum klasörleri ölçülüyor, bu biraz sürebilir...");
        Console.WriteLine();

        var sizes = new Dictionary<string, long>();
        var progress = new Progress<ProgramSize>(size => sizes[size.ProgramId] = size.Bytes);

        await new ProgramSizeCalculator().MeasureAsync(programs, progress);

        return programs
            .Select(p => sizes.TryGetValue(p.Id, out long bytes) ? p with { MeasuredSizeBytes = bytes } : p)
            .ToArray();
    }

    private static string? ValueAfter(string[] args, string flag)
    {
        int index = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
