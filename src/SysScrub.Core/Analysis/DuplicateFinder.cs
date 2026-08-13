using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SysScrub.Core.Formatting;

namespace SysScrub.Core.Analysis;

/// <summary>Aynı içeriğe sahip dosyalar kümesi.</summary>
public sealed record DuplicateGroup
{
    public required IReadOnlyList<string> Paths { get; init; }

    public required long SizeBytes { get; init; }

    /// <summary>Bir kopya korunduğunda kazanılacak alan.</summary>
    public long RecoverableBytes => SizeBytes * (Paths.Count - 1);

    public string SizeLabel => ByteSize.Format(SizeBytes);

    public string RecoverableLabel => ByteSize.Format(RecoverableBytes);
}

public sealed record DuplicateScanResult
{
    public required IReadOnlyList<DuplicateGroup> Groups { get; init; }

    public required TimeSpan Duration { get; init; }

    public required int FilesCompared { get; init; }

    /// <summary>Tam karşılaştırma yapılan dosya sayısı; işin pahalı kısmı.</summary>
    public int FilesHashed { get; init; }

    public static DuplicateScanResult Empty { get; } = new()
    {
        Groups = [],
        Duration = TimeSpan.Zero,
        FilesCompared = 0
    };

    public long RecoverableBytes => Groups.Sum(g => g.RecoverableBytes);

    public int DuplicateCount => Groups.Sum(g => g.Paths.Count - 1);
}

public sealed record DuplicateScanProgress(string Stage, int Processed, int Total);

/// <summary>
/// Yinelenen dosya bulucu.
///
/// Üç aşamalı, çünkü her dosyanın tamamını okumak kabul edilemez derecede pahalı:
///
///   1. Boyuta göre gruplama — tek başına eşsiz boyutta olan dosya kesinlikle
///      yinelenen değil. Disk okuması yok, milyonlarca dosyayı saniyeler içinde eler.
///   2. Baş ve son 4 KB özeti — farklı dosyalar neredeyse her zaman burada ayrılır.
///      Dosya başına iki küçük okuma.
///   3. Tam özet — yalnızca ilk iki aşamadan geçenler için.
///
/// Hiçbir şey silmiyor: yalnızca rapor üretiyor. Silme kararı ve "her gruptan en az
/// bir kopya korunur" kilidi çağıran katmanda.
/// </summary>
public sealed class DuplicateFinder(ILogger<DuplicateFinder>? logger = null)
{
    /// <summary>Baş ve sondan okunan parça boyutu.</summary>
    private const int SampleSize = 4096;

    /// <summary>Bu boyutun altındaki dosyalar kazanç getirmiyor, listeyi boğuyor.</summary>
    private const long MinimumFileSize = 1024 * 1024;

    private readonly ILogger _logger = logger ?? NullLogger<DuplicateFinder>.Instance;

    /// <summary>Taranmış ağaçtaki dosyalar arasında yinelenenleri bulur.</summary>
    public Task<DuplicateScanResult> FindAsync(
        FolderNode root,
        IProgress<DuplicateScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);

        return Task.Run(() => Find(Collect(root), progress, cancellationToken), cancellationToken);
    }

    /// <summary>Ağaçtaki dosyaları düzleştirir.</summary>
    private static List<(string Path, long Size)> Collect(FolderNode root)
    {
        var files = new List<(string, long)>();
        var stack = new Stack<FolderNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            FolderNode node = stack.Pop();

            foreach (FolderNode child in node.Children)
            {
                if (child.IsFile)
                {
                    if (child.SizeBytes >= MinimumFileSize)
                    {
                        files.Add((child.FullPath, child.SizeBytes));
                    }
                }
                else
                {
                    stack.Push(child);
                }
            }
        }

        return files;
    }

    private DuplicateScanResult Find(
        List<(string Path, long Size)> files,
        IProgress<DuplicateScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        // 1. aşama: boyut. Tek başına kalan boyut kesinlikle yinelenen değil.
        List<IGrouping<long, (string Path, long Size)>> sizeGroups = files
            .GroupBy(f => f.Size)
            .Where(g => g.Count() > 1)
            .ToList();

        int candidates = sizeGroups.Sum(g => g.Count());

        progress?.Report(new DuplicateScanProgress("Boyutlar karşılaştırılıyor", 0, candidates));

        // 2. aşama: baş ve son parçanın özeti.
        var sampleGroups = new Dictionary<string, List<(string Path, long Size)>>(StringComparer.Ordinal);
        int processed = 0;

        foreach (IGrouping<long, (string Path, long Size)> group in sizeGroups)
        {
            foreach ((string path, long size) in group)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? sample = SampleHash(path, size);
                processed++;

                if (sample is null)
                {
                    continue;
                }

                // Anahtara boyut da giriyor: farklı boyutlu dosyalar aynı örneği taşıyabilir.
                string key = $"{size}:{sample}";

                if (!sampleGroups.TryGetValue(key, out List<(string, long)>? bucket))
                {
                    sampleGroups[key] = bucket = [];
                }

                bucket.Add((path, size));

                if (processed % 50 == 0)
                {
                    progress?.Report(new DuplicateScanProgress("İçerik örnekleniyor", processed, candidates));
                }
            }
        }

        // 3. aşama: tam özet, yalnızca hâlâ aday olanlar için.
        var groups = new List<DuplicateGroup>();
        int hashed = 0;

        List<(string Path, long Size)>[] survivors = sampleGroups.Values.Where(b => b.Count > 1).ToArray();
        int toHash = survivors.Sum(b => b.Count);

        foreach (List<(string Path, long Size)> bucket in survivors)
        {
            var byHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach ((string path, _) in bucket)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? hash = FullHash(path);
                hashed++;

                if (hash is null)
                {
                    continue;
                }

                if (!byHash.TryGetValue(hash, out List<string>? paths))
                {
                    byHash[hash] = paths = [];
                }

                paths.Add(path);

                if (hashed % 20 == 0)
                {
                    progress?.Report(new DuplicateScanProgress("Dosyalar karşılaştırılıyor", hashed, toHash));
                }
            }

            foreach (List<string> paths in byHash.Values.Where(p => p.Count > 1))
            {
                groups.Add(new DuplicateGroup
                {
                    Paths = paths.OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase).ToArray(),
                    SizeBytes = bucket[0].Size
                });
            }
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Yinelenen tarama: {Files} aday, {Hashed} tam karşılaştırma, {Groups} grup, {Elapsed} ms",
            candidates, hashed, groups.Count, stopwatch.ElapsedMilliseconds);

        return new DuplicateScanResult
        {
            Groups = groups.OrderByDescending(g => g.RecoverableBytes).ToArray(),
            Duration = stopwatch.Elapsed,
            FilesCompared = candidates,
            FilesHashed = hashed
        };
    }

    /// <summary>
    /// Baş ve son parçanın özeti. Son parça da alınıyor çünkü aynı başlıkla
    /// başlayan dosyalar (aynı biçimin farklı içerikleri) sadece baştan ayrılmıyor.
    /// </summary>
    private static string? SampleHash(string path, long size)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            using var hash = SHA256.Create();

            var buffer = new byte[SampleSize];

            int read = stream.Read(buffer, 0, SampleSize);
            hash.TransformBlock(buffer, 0, read, null, 0);

            if (size > SampleSize * 2)
            {
                stream.Seek(-SampleSize, SeekOrigin.End);
                read = stream.Read(buffer, 0, SampleSize);
                hash.TransformBlock(buffer, 0, read, null, 0);
            }

            hash.TransformFinalBlock([], 0, 0);

            return Convert.ToHexString(hash.Hash!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Okunamayan dosya aday olmaktan çıkar; yinelenen sayılmaz.
            return null;
        }
    }

    private static string? FullHash(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);

            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }
}
