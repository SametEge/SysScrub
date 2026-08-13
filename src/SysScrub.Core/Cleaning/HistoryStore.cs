using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SysScrub.Core.Machine;

namespace SysScrub.Core.Cleaning;

/// <summary>Sistemde yapılan bir değişikliğin türü. Sonraki fazlarda registry ve sürücü de eklenecek.</summary>
public enum HistoryOperation
{
    Clean,
    RegistryClean,
    DriverUpdate,
    StartupChange,
    Uninstall
}

/// <summary>
/// Zaman tünelindeki bir satır. Kullanıcının "ne zaman ne yaptım, geri alabilir miyim"
/// sorusunun tek cevabı bu kayıt.
/// </summary>
public sealed record HistoryRun
{
    public required Guid RunId { get; init; }

    public required HistoryOperation Operation { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required TimeSpan Duration { get; init; }

    public required long BytesFreed { get; init; }

    public required int ItemsAffected { get; init; }

    public int ItemsFailed { get; init; }

    public int ItemsScheduledForReboot { get; init; }

    /// <summary>Karantinada duruyorsa geri alınabilir. Kalıcı silinenler için false.</summary>
    public bool IsReversible { get; init; }

    public bool WasReverted { get; init; }

    /// <summary>Kanıtlı öncesi/sonrası: sistem diskinin işlemden önceki ve sonraki boş alanı.</summary>
    public long FreeSpaceBefore { get; init; }

    public long FreeSpaceAfter { get; init; }

    public IReadOnlyList<string> RuleIds { get; init; } = [];

    /// <summary>Registry temizliklerinde geri yüklemede kullanılacak .reg dosyası.</summary>
    public string? BackupPath { get; init; }

    [JsonIgnore]
    public long MeasuredGain => FreeSpaceAfter - FreeSpaceBefore;
}

/// <summary>Bir çalıştırmada işlenen tek öğe. Ayrıntı görünümü için ayrı dosyada tutulur.</summary>
public sealed record HistoryItem
{
    public required string Path { get; init; }

    public required string RuleId { get; init; }

    public required long Bytes { get; init; }

    public required HistoryItemOutcome Outcome { get; init; }

    public string? Message { get; init; }
}

public enum HistoryItemOutcome
{
    Deleted,
    Quarantined,
    RecycleBin,
    ScheduledForReboot,
    SkippedByGuard,
    Failed,

    // Yeni değerler sona eklenir: geçmiş dosyalarında sayı olarak saklanıyorlar,
    // araya girmek eski kayıtların anlamını değiştirir.

    /// <summary>Silinmedi, durumu değiştirildi (başlangıç öğesi açma/kapama gibi).</summary>
    Changed
}

/// <summary>
/// İşlem geçmişi. Özetler tek bir JSON-lines dosyasında (hızlı listeleme),
/// ayrıntılar çalıştırma başına ayrı dosyada (istendiğinde okunur) tutulur.
///
/// JSON-lines seçilmesinin sebebi: dosyanın sonuna eklemek atomik ve ucuz,
/// bozulan bir satır kalan geçmişi okunamaz hale getirmiyor.
/// </summary>
public sealed class HistoryStore
{
    private const string RunsFileName = "runs.jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _directory;
    private readonly ILogger _logger;
    private readonly object _writeLock = new();

    public HistoryStore(string? directory = null, ILogger<HistoryStore>? logger = null)
    {
        _directory = directory ?? Path.Combine(AppPaths.DataDirectory, "history");
        _logger = logger ?? NullLogger<HistoryStore>.Instance;
    }

    public void Append(HistoryRun run, IReadOnlyList<HistoryItem> items)
    {
        try
        {
            Directory.CreateDirectory(_directory);

            lock (_writeLock)
            {
                File.AppendAllText(
                    Path.Combine(_directory, RunsFileName),
                    JsonSerializer.Serialize(run, JsonOptions) + Environment.NewLine);
            }

            if (items.Count > 0)
            {
                using var writer = new StreamWriter(ItemsPath(run.RunId), append: false);

                foreach (HistoryItem item in items)
                {
                    writer.WriteLine(JsonSerializer.Serialize(item, JsonOptions));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Geçmiş yazılamadıysa temizlik yine de yapılmış olur; kullanıcıyı durdurmayız.
            _logger.LogWarning(ex, "İşlem geçmişi yazılamadı: {RunId}", run.RunId);
        }
    }

    public IReadOnlyList<HistoryRun> ListRuns(int limit = 200)
    {
        string path = Path.Combine(_directory, RunsFileName);

        if (!File.Exists(path))
        {
            return [];
        }

        var runs = new List<HistoryRun>();

        try
        {
            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    if (JsonSerializer.Deserialize<HistoryRun>(line, JsonOptions) is { } run)
                    {
                        runs.Add(run);
                    }
                }
                catch (JsonException)
                {
                    // Bozuk tek satır kalan geçmişi düşürmez.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "İşlem geçmişi okunamadı");
            return [];
        }

        // Aynı çalıştırma geri alındığında yeni satır eklenir; en son yazılan geçerlidir.
        return runs
            .GroupBy(r => r.RunId)
            .Select(g => g.Last())
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToArray();
    }

    public IReadOnlyList<HistoryItem> ListItems(Guid runId)
    {
        string path = ItemsPath(runId);

        if (!File.Exists(path))
        {
            return [];
        }

        var items = new List<HistoryItem>();

        try
        {
            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    if (JsonSerializer.Deserialize<HistoryItem>(line, JsonOptions) is { } item)
                    {
                        items.Add(item);
                    }
                }
                catch (JsonException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return items;
    }

    /// <summary>Geri alma sonrası çalıştırmayı yeniden yazar; son satır kazanır.</summary>
    public void MarkReverted(Guid runId)
    {
        HistoryRun? run = ListRuns(int.MaxValue).FirstOrDefault(r => r.RunId == runId);

        if (run is null)
        {
            return;
        }

        Append(run with { WasReverted = true, IsReversible = false }, []);
    }

    private string ItemsPath(Guid runId) => Path.Combine(_directory, $"{runId:N}.jsonl");
}
