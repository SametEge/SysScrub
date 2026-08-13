using SysScrub.Core.Analysis;
using SysScrub.Core.Formatting;

namespace SysScrub.Cli.Commands;

/// <summary>
/// Disk analizi. Salt-okunur: hiçbir dosya silinmez, taşınmaz, açılmaz.
/// Bulut yer tutucuları indirilmez.
/// </summary>
internal static class AnalyzeCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        string path = args.Skip(1).FirstOrDefault(a => !a.StartsWith('-'))
                      ?? Path.GetPathRoot(Environment.SystemDirectory)
                      ?? @"C:\";

        bool duplicates = args.Contains("--duplicates", StringComparer.OrdinalIgnoreCase);
        int top = 20;

        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Klasör bulunamadı: {path}");
            return 1;
        }

        Console.WriteLine($"Taranıyor: {path}");
        Console.WriteLine();

        DiskScanResult result = await new DiskScanner().ScanAsync(path);

        Console.WriteLine($"EN BÜYÜK KLASÖRLER");

        foreach (FolderNode folder in result.Root.Children.Where(c => !c.IsFile).Take(top))
        {
            Console.WriteLine($"  {folder.SizeLabel,10}  %{folder.ShareOfParent * 100,5:F1}  {folder.Name}");
        }

        Console.WriteLine();
        Console.WriteLine("EN BÜYÜK DOSYALAR");

        foreach (FolderNode file in result.LargestFiles.Take(top))
        {
            Console.WriteLine($"  {file.SizeLabel,10}  {file.FullPath}");
        }

        Console.WriteLine();
        Console.WriteLine("TÜR DAĞILIMI");

        foreach (FileTypeSummary type in result.TypeBreakdown.Take(top))
        {
            Console.WriteLine($"  {type.SizeLabel,10}  {type.Count,8:N0} dosya  {type.Label}");
        }

        Console.WriteLine();
        Console.WriteLine(new string('-', 60));
        Console.WriteLine(
            $"{ByteSize.Format(result.TotalBytes)} · {result.FileCount:N0} dosya · " +
            $"{result.DirectoryCount:N0} klasör · {result.Duration.TotalSeconds:F1} sn");

        if (result.SkippedDirectories > 0 || result.CloudPlaceholders > 0 || result.SkippedLinks > 0)
        {
            Console.WriteLine(
                $"Atlanan: {result.SkippedDirectories:N0} erişilemeyen klasör · " +
                $"{result.CloudPlaceholders:N0} bulut yer tutucusu · " +
                $"{result.SkippedLinks:N0} bağlantı noktası");
        }

        if (duplicates)
        {
            await PrintDuplicatesAsync(result.Root, top);
        }

        return 0;
    }

    private static async Task PrintDuplicatesAsync(FolderNode root, int top)
    {
        Console.WriteLine();
        Console.WriteLine("Yinelenen dosyalar aranıyor (1 MB üstü)...");
        Console.WriteLine();

        DuplicateScanResult result = await new DuplicateFinder().FindAsync(root);

        foreach (DuplicateGroup group in result.Groups.Take(top))
        {
            Console.WriteLine($"  {group.SizeLabel,10}  × {group.Paths.Count}  (kazanç {group.RecoverableLabel})");

            foreach (string file in group.Paths)
            {
                Console.WriteLine($"              {file}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            $"{result.Groups.Count:N0} grup · {result.DuplicateCount:N0} fazla kopya · " +
            $"kazanılabilir {ByteSize.Format(result.RecoverableBytes)} · " +
            $"{result.FilesHashed:N0} dosya tam karşılaştırıldı · {result.Duration.TotalSeconds:F1} sn");
    }
}
