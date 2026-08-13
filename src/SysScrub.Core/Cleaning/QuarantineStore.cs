using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SysScrub.Core.Machine;
using SysScrub.Core.Safety;
using SysScrub.Core.Formatting;

namespace SysScrub.Core.Cleaning;

/// <summary>Karantinaya alınmış tek bir dosyanın kaydı.</summary>
public sealed record QuarantineEntry
{
    public required string OriginalPath { get; init; }

    public required string StoredName { get; init; }

    public required string RuleId { get; init; }

    public required long Bytes { get; init; }

    public required DateTime OriginalLastWriteUtc { get; init; }
}

/// <summary>Bir temizlik çalıştırmasının karantina bildirimi.</summary>
public sealed record QuarantineManifest
{
    public required Guid RunId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required IReadOnlyList<QuarantineEntry> Entries { get; init; }

    [JsonIgnore]
    public long TotalBytes => Entries.Sum(e => e.Bytes);
}

/// <summary>
/// Karantina: silinen dosyalar hemen yok edilmez, saklama süresi boyunca geri alınabilir
/// bir kenara taşınır.
///
/// Bu, uygulamanın "hiçbir şey geri alınamaz değil" iddiasının dosya tarafındaki karşılığı.
/// Bir kural yanlış yazılmış olsa bile kullanıcı kaybettiğini geri alabilir.
/// </summary>
public sealed class QuarantineStore
{
    private const string ManifestFileName = "manifest.json";
    private const string FilesFolderName = "files";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _rootDirectory;
    private readonly ILogger _logger;

    public QuarantineStore(string? rootDirectory = null, ILogger<QuarantineStore>? logger = null)
    {
        _rootDirectory = rootDirectory ?? AppPaths.QuarantineDirectory;
        _logger = logger ?? NullLogger<QuarantineStore>.Instance;
    }

    public string RootDirectory => _rootDirectory;

    public QuarantineSession BeginSession(Guid runId) => new(this, runId);

    internal string GetRunDirectory(Guid runId) => Path.Combine(_rootDirectory, runId.ToString("N"));

    internal string GetFilesDirectory(Guid runId) => Path.Combine(GetRunDirectory(runId), FilesFolderName);

    /// <summary>Saklanan tüm çalıştırmalar, en yeniden eskiye.</summary>
    public IReadOnlyList<QuarantineManifest> List()
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return [];
        }

        var manifests = new List<QuarantineManifest>();

        foreach (string directory in Directory.EnumerateDirectories(_rootDirectory))
        {
            if (TryReadManifest(directory, out QuarantineManifest? manifest))
            {
                manifests.Add(manifest);
            }
        }

        return manifests.OrderByDescending(m => m.CreatedAt).ToArray();
    }

    public QuarantineManifest? Find(Guid runId) =>
        TryReadManifest(GetRunDirectory(runId), out QuarantineManifest? manifest) ? manifest : null;

    /// <summary>
    /// Karantinadaki dosyaları asıl yerlerine geri koyar.
    /// Hedefte aynı adla bir dosya varsa üzerine yazılmaz — kullanıcının yeni verisi korunur.
    /// </summary>
    public RestoreResult Restore(Guid runId)
    {
        QuarantineManifest? manifest = Find(runId);

        if (manifest is null)
        {
            return new RestoreResult(0, 0, 0, [CoreText.Get("Cl_E_NoRun", "Karantina kaydı bulunamadı.")]);
        }

        string filesDirectory = GetFilesDirectory(runId);
        int restored = 0;
        int skipped = 0;
        long bytes = 0;
        var errors = new List<string>();

        foreach (QuarantineEntry entry in manifest.Entries)
        {
            string stored = Path.Combine(filesDirectory, entry.StoredName);

            if (!File.Exists(stored))
            {
                skipped++;
                continue;
            }

            try
            {
                if (File.Exists(entry.OriginalPath))
                {
                    skipped++;
                    continue;
                }

                string? parent = Path.GetDirectoryName(entry.OriginalPath);

                if (parent is not null)
                {
                    Directory.CreateDirectory(parent);
                }

                File.Move(stored, entry.OriginalPath);
                File.SetLastWriteTimeUtc(entry.OriginalPath, entry.OriginalLastWriteUtc);

                restored++;
                bytes += entry.Bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{entry.OriginalPath}: {ex.Message}");
            }
        }

        if (errors.Count == 0 && skipped == 0)
        {
            Delete(runId);
        }

        _logger.LogInformation("Karantina geri yüklendi {RunId}: {Restored} dosya, {Bytes} bayt", runId, restored, bytes);

        return new RestoreResult(restored, skipped, bytes, errors);
    }

    /// <summary>Saklama süresi dolmuş çalıştırmaları kalıcı olarak siler.</summary>
    public int Purge(TimeSpan retention)
    {
        DateTimeOffset cutoff = DateTimeOffset.Now - retention;
        int removed = 0;

        foreach (QuarantineManifest manifest in List())
        {
            if (manifest.CreatedAt <= cutoff && Delete(manifest.RunId))
            {
                removed++;
            }
        }

        return removed;
    }

    public bool Delete(Guid runId)
    {
        string directory = GetRunDirectory(runId);

        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Karantina silinemedi: {RunId}", runId);
            return false;
        }
    }

    public long TotalBytes() => List().Sum(m => m.TotalBytes);

    internal void WriteManifest(QuarantineManifest manifest)
    {
        string directory = GetRunDirectory(manifest.RunId);
        Directory.CreateDirectory(directory);

        File.WriteAllText(
            Path.Combine(directory, ManifestFileName),
            JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static bool TryReadManifest(string directory, out QuarantineManifest manifest)
    {
        manifest = null!;
        string path = Path.Combine(directory, ManifestFileName);

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            QuarantineManifest? parsed = JsonSerializer.Deserialize<QuarantineManifest>(
                File.ReadAllText(path), JsonOptions);

            if (parsed is null)
            {
                return false;
            }

            manifest = parsed;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

public sealed record RestoreResult(int Restored, int Skipped, long Bytes, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>
/// Tek bir temizlik çalıştırmasının karantina oturumu.
/// Dosyalar taşındıkça biriktirilir, sonunda bildirim tek seferde yazılır.
/// </summary>
public sealed class QuarantineSession
{
    private readonly QuarantineStore _store;
    private readonly List<QuarantineEntry> _entries = [];

    // Temizlik paralel çalışıyor; liste ve sayaç birden fazla iş parçacığından besleniyor.
    private readonly object _lock = new();

    private int _counter;

    internal QuarantineSession(QuarantineStore store, Guid runId)
    {
        _store = store;
        RunId = runId;
    }

    public Guid RunId { get; }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Dosyayı karantinaya taşır. Başarısızlıkta sebebi döner.</summary>
    public bool TryStore(string path, string ruleId, long bytes, DateTime lastWriteUtc, out string? error)
    {
        error = null;

        try
        {
            string filesDirectory = _store.GetFilesDirectory(RunId);
            Directory.CreateDirectory(filesDirectory);

            string storedName;

            lock (_lock)
            {
                // Sıralı ad: uzun yol sorununu ve aynı adlı dosyaların çakışmasını önler.
                storedName = $"{++_counter:D6}{Path.GetExtension(path)}";
            }

            string destination = Path.Combine(filesDirectory, storedName);

            File.Move(path, destination, overwrite: true);

            lock (_lock)
            {
                _entries.Add(new QuarantineEntry
                {
                    OriginalPath = path,
                    StoredName = storedName,
                    RuleId = ruleId,
                    Bytes = bytes,
                    OriginalLastWriteUtc = lastWriteUtc
                });
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Bildirimi yazar. Hiç dosya taşınmadıysa boş klasör bırakmaz.</summary>
    public QuarantineManifest? Commit()
    {
        QuarantineEntry[] entries;

        lock (_lock)
        {
            entries = _entries.ToArray();
        }

        if (entries.Length == 0)
        {
            _store.Delete(RunId);
            return null;
        }

        var manifest = new QuarantineManifest
        {
            RunId = RunId,
            CreatedAt = DateTimeOffset.Now,
            Entries = entries
        };

        _store.WriteManifest(manifest);
        return manifest;
    }
}
