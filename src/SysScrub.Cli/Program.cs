using SysScrub.Cli.Commands;

namespace SysScrub.Cli;

/// <summary>
/// Komut satırı arayüzü. Zamanlanmış/sessiz temizlik ve teknisyen raporu buradan çalışır.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "info" => InfoCommand.Run(),
                "rules" => RulesCommand.Run(args),
                "scan" => await ScanCommand.RunAsync(args),
                "clean" => await CleanCommand.RunAsync(args),
                "registry" => await RegistryCommand.RunAsync(args),
                "drivers" => await DriversCommand.RunAsync(args),
                "startup" => await StartupCommand.RunAsync(args),
                "programs" => await ProgramsCommand.RunAsync(args),
                "history" => HistoryCommand.List(),
                "undo" => HistoryCommand.Undo(args),
                "version" => PrintVersion(),
                _ => UnknownCommand(args[0])
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("İşlem iptal edildi.");
            return 130;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("SysScrub komut satırı");
        Console.WriteLine();
        Console.WriteLine("Kullanım: sysscrub-cli <komut> [seçenekler]");
        Console.WriteLine();
        Console.WriteLine("Komutlar:");
        Console.WriteLine("  info                Sistem özetini yazdırır");
        Console.WriteLine("  rules               Yüklü temizleme kurallarını listeler");
        Console.WriteLine("  scan                Temizlenebilir dosyaları tarar, hiçbir şey silmez");
        Console.WriteLine("  clean               Temizler. --apply verilmezse yalnızca ne olacağını gösterir");
        Console.WriteLine("  registry            Ölü kayıt defteri girdilerini tarar (--apply ile temizler)");
        Console.WriteLine("  drivers             Donanım envanterini ve eski sürücüleri listeler");
        Console.WriteLine("  startup             Açılışta çalışan öğeleri listeler (--disable/--enable <ad>)");
        Console.WriteLine("  programs            Kurulu programları listeler (--size ile gerçek boyut)");
        Console.WriteLine("  history             Geçmiş temizlikleri listeler");
        Console.WriteLine("  undo <kimlik>       Bir temizliği geri alır (karantinadan geri yükler)");
        Console.WriteLine("  version             Sürüm bilgisini yazdırır");
        Console.WriteLine();
        Console.WriteLine("Seçenekler:");
        Console.WriteLine("  --all               Varsayılan kapalı kuralları / kapalı başlangıç öğelerini de dahil et");
        Console.WriteLine("  --apply             clean komutunda gerçekten sil (varsayılan: kuru çalıştırma)");
        Console.WriteLine("  --yes               Onay sorma");
        Console.WriteLine("  --verbose           Ayrıntı göster (kural kökleri / en büyük dosyalar)");
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
        Console.Error.WriteLine("Komut listesi için: sysscrub-cli --help");
        return 1;
    }
}
