using SysScrub.Core.Cleaning;
using SysScrub.Core.Formatting;

namespace SysScrub.Cli.Commands;

/// <summary>Zaman tünelinin komut satırı karşılığı: geçmişi listeler ve geri alır.</summary>
internal static class HistoryCommand
{
    public static int List()
    {
        IReadOnlyList<HistoryRun> runs = new HistoryStore().ListRuns(50);

        if (runs.Count == 0)
        {
            Console.WriteLine("Henüz kayıtlı bir işlem yok.");
            return 0;
        }

        Console.WriteLine($"{"Tarih",-20} {"Kazanç",10}  {"Öğe",6}  Durum       Kimlik");
        Console.WriteLine(new string('-', 88));

        foreach (HistoryRun run in runs)
        {
            string state = run.WasReverted ? "geri alındı"
                : run.IsReversible ? "geri alınabilir"
                : "kalıcı";

            Console.WriteLine(
                $"{run.StartedAt.LocalDateTime,-20:dd.MM.yyyy HH:mm} " +
                $"{ByteSize.Format(run.BytesFreed),10}  {run.ItemsAffected,6}  {state,-15} {run.RunId}");
        }

        return 0;
    }

    public static int Undo(string[] args)
    {
        if (args.Length < 2 || !Guid.TryParse(args[1], out Guid runId))
        {
            Console.Error.WriteLine("Kullanım: sysscrub-cli undo <çalıştırma-kimliği>");
            Console.Error.WriteLine("Kimlikleri görmek için: sysscrub-cli history");
            return 1;
        }

        var quarantine = new QuarantineStore();
        RestoreResult result = quarantine.Restore(runId);

        if (result.Restored == 0 && result.Errors.Count > 0)
        {
            foreach (string error in result.Errors)
            {
                Console.Error.WriteLine(error);
            }

            return 1;
        }

        new HistoryStore().MarkReverted(runId);

        Console.WriteLine($"{result.Restored} dosya geri yüklendi ({ByteSize.Format(result.Bytes)}).");

        if (result.Skipped > 0)
        {
            Console.WriteLine($"{result.Skipped} dosya atlandı — hedefte zaten bir dosya var.");
        }

        foreach (string error in result.Errors)
        {
            Console.Error.WriteLine(error);
        }

        return 0;
    }
}
