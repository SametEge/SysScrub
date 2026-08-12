using System.Diagnostics;
using System.IO.Enumeration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SysScrub.Core.Rules;
using SysScrub.Core.Safety;
using SysScrub.Core.Windows;

namespace SysScrub.Core.Cleaning;

/// <summary>
/// Salt-okunur tarama. Hiçbir şeyi değiştirmez, yalnızca ne silinebileceğini listeler.
///
/// Tarama ve silmenin ayrı olması bilinçli: kullanıcı ne olacağını gördükten sonra
/// karar verir, ve tarama tamamen zararsız olduğu için istediği kadar çalıştırabilir.
/// </summary>
public sealed class ScanEngine
{
    private readonly PathResolver _resolver;
    private readonly SafetyGuard _guard;
    private readonly ILogger<ScanEngine> _logger;

    public ScanEngine(PathResolver resolver, SafetyGuard guard, ILogger<ScanEngine>? logger = null)
    {
        _resolver = resolver;
        _guard = guard;
        _logger = logger ?? NullLogger<ScanEngine>.Instance;
    }

    public async Task<ScanReport> ScanAsync(
        RuleSet ruleSet,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        DateTimeOffset startedAt = DateTimeOffset.Now;

        CleaningRule[] selected = ruleSet.Rules.Where(options.IsEnabled).ToArray();
        CleaningRule[] runnable = selected.Where(r => options.IsElevated || !r.RequiresAdmin).ToArray();
        int skippedForElevation = selected.Length - runnable.Length;

        HashSet<string> runningProcesses = GetRunningProcessNames();

        var results = new RuleScanResult[runnable.Length];
        int completed = 0;
        long bytesFound = 0;
        int filesFound = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, runnable.Length),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, options.MaxParallelism),
                CancellationToken = cancellationToken
            },
            (index, token) =>
            {
                CleaningRule rule = runnable[index];

                RuleScanResult result = ScanRule(rule, runningProcesses, token);
                results[index] = result;

                Interlocked.Add(ref bytesFound, result.Bytes);
                Interlocked.Add(ref filesFound, result.Count);
                int done = Interlocked.Increment(ref completed);

                progress?.Report(new ScanProgress
                {
                    CurrentRule = rule.Name.Resolve(),
                    CompletedRules = done,
                    TotalRules = runnable.Length,
                    BytesFound = Interlocked.Read(ref bytesFound),
                    FilesFound = Volatile.Read(ref filesFound)
                });

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

        stopwatch.Stop();

        _logger.LogInformation(
            "Tarama bitti: {Rules} kural, {Files} dosya, {Bytes} bayt, {Elapsed} ms",
            runnable.Length, filesFound, bytesFound, stopwatch.ElapsedMilliseconds);

        return new ScanReport
        {
            Results = results.Where(r => r is not null).ToArray(),
            StartedAt = startedAt,
            Duration = stopwatch.Elapsed,
            SkippedForElevation = skippedForElevation
        };
    }

    private RuleScanResult ScanRule(CleaningRule rule, HashSet<string> runningProcesses, CancellationToken token)
    {
        string[] blockers = rule.BlockingProcesses
            .Where(runningProcesses.Contains)
            .ToArray();

        if (rule.Handler is "recycleBin")
        {
            return ScanRecycleBin(rule, blockers);
        }

        var items = new List<ScanItem>();
        bool anyTarget = false;

        foreach (RuleRoot root in rule.Roots)
        {
            foreach (string directory in _resolver.Resolve(root.Base, root.Path))
            {
                token.ThrowIfCancellationRequested();
                anyTarget = true;

                CollectFiles(rule, directory, items, token);
            }
        }

        return new RuleScanResult
        {
            Rule = rule,
            Items = items,
            RunningBlockers = blockers,
            NoTargets = !anyTarget
        };
    }

    private RuleScanResult ScanRecycleBin(CleaningRule rule, string[] blockers)
    {
        var items = new List<ScanItem>();

        foreach (string driveRoot in _resolver.GetBasePaths(PathToken.AllFixedDrives))
        {
            RecycleBinInfo info = RecycleBin.Query(driveRoot);

            if (info.IsEmpty)
            {
                continue;
            }

            items.Add(new ScanItem
            {
                Path = Path.Combine(driveRoot, "$Recycle.Bin"),
                Bytes = info.Bytes,
                LastWriteUtc = DateTime.UtcNow,
                AllowedRoot = driveRoot,
                IsHandlerItem = true
            });
        }

        return new RuleScanResult { Rule = rule, Items = items, RunningBlockers = blockers };
    }

    private void CollectFiles(CleaningRule rule, string rootDirectory, List<ScanItem> items, CancellationToken token)
    {
        DateTime cutoffUtc = rule.MinAgeDays > 0
            ? DateTime.UtcNow.AddDays(-rule.MinAgeDays)
            : DateTime.MaxValue;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = rule.Recursive,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.System
        };

        FileSystemEnumerable<ScanEntry> enumerable;

        try
        {
            enumerable = new FileSystemEnumerable<ScanEntry>(rootDirectory, TransformEntry, options)
            {
                ShouldIncludePredicate = static (ref FileSystemEntry entry) => !entry.IsDirectory,

                // Bağlantı noktalarının içine girilmez: hedefteki veri bu kuralın kapsamı değil.
                ShouldRecursePredicate = static (ref FileSystemEntry entry) =>
                    !entry.Attributes.HasFlag(FileAttributes.ReparsePoint)
            };
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            return;
        }

        using IEnumerator<ScanEntry> enumerator = enumerable.GetEnumerator();

        while (true)
        {
            token.ThrowIfCancellationRequested();

            ScanEntry entry;

            try
            {
                if (!enumerator.MoveNext())
                {
                    break;
                }

                entry = enumerator.Current;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Dolaşım sırasında erişilemeyen bir dal çıkarsa tarama tümden düşmemeli.
                _logger.LogDebug(ex, "{Rule}: {Directory} dolaşılırken atlandı", rule.Id, rootDirectory);
                break;
            }

            if (rule.MinAgeDays > 0 && entry.LastWriteUtc > cutoffUtc)
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(rootDirectory, entry.Path);

            if (!rule.Matches(relativePath))
            {
                continue;
            }

            if (!_guard.InspectFile(entry.Path, rootDirectory, entry.Attributes).IsAllowed)
            {
                continue;
            }

            items.Add(new ScanItem
            {
                Path = entry.Path,
                Bytes = entry.Length,
                LastWriteUtc = entry.LastWriteUtc,
                AllowedRoot = rootDirectory
            });
        }
    }

    private static ScanEntry TransformEntry(ref FileSystemEntry entry) =>
        new(entry.ToFullPath(), entry.Length, entry.Attributes, entry.LastWriteTimeUtc.UtcDateTime);

    /// <summary>
    /// Çalışan süreç adları. Kural başına Process.GetProcesses çağırmak pahalı olduğu için
    /// tarama başında bir kez alınır.
    /// </summary>
    private static HashSet<string> GetRunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    names.Add(process.ProcessName);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Süreç listesi alınamazsa engelleyici uyarısı gösterilmez, tarama yine çalışır.
        }

        return names;
    }

    private readonly record struct ScanEntry(string Path, long Length, FileAttributes Attributes, DateTime LastWriteUtc);
}
