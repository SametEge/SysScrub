using System.Diagnostics;
using System.IO.Enumeration;
using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SysScrub.Core.Analysis;

/// <summary>
/// Klasör ağacını tarayıp boyut dağılımını çıkarır.
///
/// Üç şeyi bilerek yapmıyor:
///
///   Bağlantı noktalarının (junction/symlink) içine girmiyor. Girseydi aynı ağaç
///   iki kez sayılır, kötü durumda sonsuz döngüye girerdi.
///
///   Bulut yer tutucularını indirmiyor ve boyuta katmıyor. OneDrive'da duran ama
///   diske inmemiş bir dosya yer kaplamıyor; boyuta katmak "alanı ne yiyor"
///   sorusunun cevabını yanlış yapardı.
///
///   Erişemediği klasörü sessizce yutmuyor; sayıyor ve özette bildiriyor.
/// </summary>
public sealed class DiskScanner(ILogger<DiskScanner>? logger = null)
{
    /// <summary>En büyük dosyalar listesinde tutulan satır sayısı.</summary>
    private const int LargestFileCount = 100;

    /// <summary>İlerleme bildirimi bu aralıktan sık gönderilmiyor.</summary>
    private const int ProgressIntervalMs = 120;

    /// <summary>Bulut yer tutucusunu tanıtan öznitelikler.</summary>
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x0040_0000;

    private const FileAttributes RecallOnOpen = (FileAttributes)0x0004_0000;

    private readonly ILogger _logger = logger ?? NullLogger<DiskScanner>.Instance;

    public Task<DiskScanResult> ScanAsync(
        string rootPath,
        IProgress<DiskScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        return Task.Run(() => Scan(rootPath, progress, cancellationToken), cancellationToken);
    }

    private DiskScanResult Scan(
        string rootPath,
        IProgress<DiskScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var state = new ScanState(progress);

        var root = new FolderNode
        {
            Name = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name
                ? name
                : rootPath,
            FullPath = rootPath
        };

        if (!Directory.Exists(rootPath))
        {
            return DiskScanResult.Empty with { Root = root, Duration = stopwatch.Elapsed };
        }

        Walk(root, state, cancellationToken);

        root.SortChildren();
        stopwatch.Stop();

        _logger.LogInformation(
            "Disk analizi: {Path} · {Files} dosya · {Size} bayt · {Elapsed} ms",
            rootPath, state.Files, root.SizeBytes, stopwatch.ElapsedMilliseconds);

        return new DiskScanResult
        {
            Root = root,
            Duration = stopwatch.Elapsed,
            FileCount = state.Files,
            DirectoryCount = state.Directories,
            SkippedDirectories = state.Skipped,
            CloudPlaceholders = state.Placeholders,
            SkippedLinks = state.Links,
            LargestFiles = state.LargestFiles(LargestFileCount),
            TypeBreakdown = state.TypeBreakdown()
        };
    }

    /// <summary>
    /// Ağacı gezer. Üst seviyedeki klasörler paralel taranıyor: iş çoğunlukla
    /// dizin okumada bekliyor ve tek iş parçacığı diski doyuramıyor.
    /// </summary>
    private void Walk(FolderNode node, ScanState state, CancellationToken cancellationToken)
    {
        List<string> directories = [];

        ScanFiles(node, state, directories, cancellationToken);

        if (directories.Count == 0)
        {
            return;
        }

        var children = new FolderNode[directories.Count];

        Parallel.For(
            0,
            directories.Count,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
            i =>
            {
                var child = new FolderNode
                {
                    Name = Path.GetFileName(directories[i]),
                    FullPath = directories[i]
                };

                WalkSequential(child, state, cancellationToken);
                children[i] = child;
            });

        foreach (FolderNode child in children)
        {
            // Boş klasörler listeyi boğuyor ve treemap'te çizilemiyor.
            if (child.SizeBytes == 0 && !child.HasChildren)
            {
                continue;
            }

            node.Add(child);
            node.SizeBytes += child.SizeBytes;
            node.FileCount += child.FileCount;
        }
    }

    /// <summary>Alt seviyeler tek iş parçacığında: paralelliği derinlemesine yaymak fayda etmiyor.</summary>
    private void WalkSequential(FolderNode node, ScanState state, CancellationToken cancellationToken)
    {
        List<string> directories = [];

        ScanFiles(node, state, directories, cancellationToken);

        foreach (string directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var child = new FolderNode
            {
                Name = Path.GetFileName(directory),
                FullPath = directory
            };

            WalkSequential(child, state, cancellationToken);

            if (child.SizeBytes == 0 && !child.HasChildren)
            {
                continue;
            }

            node.Add(child);
            node.SizeBytes += child.SizeBytes;
            node.FileCount += child.FileCount;
        }
    }

    /// <summary>
    /// Tek bir klasörün içeriğini okur: dosyalar düğüme eklenir, alt klasörler
    /// listeye yazılır. Öznitelikler numaralandırma sırasında zaten geliyor,
    /// ayrı bir sistem çağrısı yapılmıyor.
    /// </summary>
    private void ScanFiles(
        FolderNode node,
        ScanState state,
        List<string> directories,
        CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            // Bilerek false: true olsaydı açılamayan klasör sessizce atlanır ve
            // sayaç hiç artmazdı. Kullanıcıya "her şey sayıldı" izlenimi vermek,
            // eksik toplamı açıklamamaktan daha kötü.
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0
        };

        try
        {
            var entries = new FileSystemEnumerable<(string Path, long Length, FileAttributes Attributes, bool IsDirectory)>(
                node.FullPath,
                (ref FileSystemEntry entry) =>
                    (entry.ToFullPath(), entry.Length, entry.Attributes, entry.IsDirectory),
                options);

            foreach ((string path, long length, FileAttributes attributes, bool isDirectory) in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Bağlantı noktası: hedefi başka yerde, oraya sıçramıyoruz.
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    state.CountLink();
                    continue;
                }

                if (isDirectory)
                {
                    directories.Add(path);
                    state.CountDirectory();
                    continue;
                }

                // Bulut yer tutucusu diskte yer kaplamıyor; boyuta katılmıyor.
                if (attributes.HasFlag(RecallOnDataAccess) ||
                    attributes.HasFlag(RecallOnOpen) ||
                    attributes.HasFlag(FileAttributes.Offline))
                {
                    state.CountPlaceholder();
                    continue;
                }

                var file = new FolderNode
                {
                    Name = Path.GetFileName(path),
                    FullPath = path,
                    SizeBytes = length,
                    IsFile = true,
                    FileCount = 1
                };

                node.Add(file);
                node.SizeBytes += length;
                node.FileCount++;

                state.CountFile(file, node.FullPath);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            state.CountSkipped();
        }
    }

    /// <summary>
    /// Tarama boyunca paylaşılan sayaçlar. Paralel gezinme yüzünden her erişim
    /// kilitli; sayaçlar ucuz olduğu için darboğaz yaratmıyor.
    /// </summary>
    private sealed class ScanState(IProgress<DiskScanProgress>? progress)
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, (long Bytes, int Count)> _types =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly List<FolderNode> _largest = [];
        private readonly Stopwatch _sinceReport = Stopwatch.StartNew();

        /// <summary>En küçük "en büyük dosya"; bundan küçükler listeye hiç girmiyor.</summary>
        private long _largestFloor;

        public int Files { get; private set; }

        public int Directories { get; private set; }

        public int Skipped { get; private set; }

        public int Placeholders { get; private set; }

        public int Links { get; private set; }

        public long Bytes { get; private set; }

        public void CountFile(FolderNode file, string currentPath)
        {
            lock (_lock)
            {
                Files++;
                Bytes += file.SizeBytes;

                string extension = Path.GetExtension(file.Name);

                _types.TryGetValue(extension, out (long Bytes, int Count) type);
                _types[extension] = (type.Bytes + file.SizeBytes, type.Count + 1);

                if (file.SizeBytes >= _largestFloor || _largest.Count < LargestFileCount)
                {
                    _largest.Add(file);

                    // Liste iki katına çıkınca budanıyor: her eklemede sıralamak pahalı.
                    if (_largest.Count > LargestFileCount * 2)
                    {
                        Trim();
                    }
                }

                Report(currentPath);
            }
        }

        public void CountDirectory()
        {
            lock (_lock)
            {
                Directories++;
            }
        }

        public void CountSkipped()
        {
            lock (_lock)
            {
                Skipped++;
            }
        }

        public void CountPlaceholder()
        {
            lock (_lock)
            {
                Placeholders++;
            }
        }

        public void CountLink()
        {
            lock (_lock)
            {
                Links++;
            }
        }

        public IReadOnlyList<FolderNode> LargestFiles(int count)
        {
            lock (_lock)
            {
                return _largest
                    .OrderByDescending(f => f.SizeBytes)
                    .Take(count)
                    .ToArray();
            }
        }

        public IReadOnlyList<FileTypeSummary> TypeBreakdown()
        {
            lock (_lock)
            {
                return _types
                    .Select(pair => new FileTypeSummary(pair.Key, pair.Value.Bytes, pair.Value.Count))
                    .OrderByDescending(t => t.SizeBytes)
                    .ToArray();
            }
        }

        private void Trim()
        {
            _largest.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
            _largest.RemoveRange(LargestFileCount, _largest.Count - LargestFileCount);
            _largestFloor = _largest[^1].SizeBytes;
        }

        private void Report(string currentPath)
        {
            if (progress is null || _sinceReport.ElapsedMilliseconds < ProgressIntervalMs)
            {
                return;
            }

            _sinceReport.Restart();
            progress.Report(new DiskScanProgress(currentPath, Files, Bytes));
        }
    }
}
