using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SysScrub.Core.Machine;
using SysScrub.Core.Rules;
using SysScrub.Core.Safety;
using SysScrub.Core.Windows;

namespace SysScrub.Core.Cleaning;

/// <summary>
/// Silme motoru. Tarama raporundaki seçili öğeleri, her birini yeniden denetleyerek siler.
///
/// Taramada denetimden geçmiş olması yeterli sayılmaz: tarama ile silme arasında
/// dosya değişmiş, yerine bir bağlantı konmuş ya da bulut yer tutucusuna dönüşmüş olabilir.
/// Denetim silmenin hemen öncesinde tekrarlanır.
/// </summary>
public sealed class CleanEngine
{
    private readonly SafetyGuard _guard;
    private readonly QuarantineStore _quarantine;
    private readonly HistoryStore _history;
    private readonly SystemInfoService _systemInfo;
    private readonly ILogger _logger;

    public CleanEngine(
        SafetyGuard guard,
        QuarantineStore quarantine,
        HistoryStore history,
        SystemInfoService systemInfo,
        ILogger<CleanEngine>? logger = null)
    {
        _guard = guard;
        _quarantine = quarantine;
        _history = history;
        _systemInfo = systemInfo;
        _logger = logger ?? NullLogger<CleanEngine>.Instance;
    }

    public async Task<CleanResult> CleanAsync(
        IReadOnlyList<RuleScanResult> selection,
        CleanOptions options,
        IProgress<CleanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(options);

        var runId = Guid.NewGuid();
        DateTimeOffset startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

        long freeSpaceBefore = GetSystemDriveFreeBytes();

        var state = new CleanState(runId, options, _quarantine);
        int total = selection.Sum(r => r.Count);

        foreach (RuleScanResult result in selection)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await CleanRuleAsync(result, state, options, progress, total, cancellationToken).ConfigureAwait(false);
        }

        state.QuarantineSession?.Commit();

        stopwatch.Stop();

        // Ölçüm, silmenin hemen ardından yapılır; dosya sistemi buna kadar boşalmış olur.
        long freeSpaceAfter = options.DryRun ? freeSpaceBefore : GetSystemDriveFreeBytes();

        var cleanResult = new CleanResult
        {
            RunId = runId,
            StartedAt = startedAt,
            Duration = stopwatch.Elapsed,
            BytesFreed = state.BytesFreed,
            Deleted = state.Deleted,
            Quarantined = state.Quarantined,
            SentToRecycleBin = state.RecycleBin,
            ScheduledForReboot = state.ScheduledForReboot,
            SkippedByGuard = state.SkippedByGuard,
            Failures = state.Failures,
            WasDryRun = options.DryRun,
            FreeSpaceBefore = freeSpaceBefore,
            FreeSpaceAfter = freeSpaceAfter
        };

        if (!options.DryRun)
        {
            WriteHistory(cleanResult, selection, state);
            _quarantine.Purge(options.QuarantineRetention);
        }

        _logger.LogInformation(
            "Temizlik bitti {RunId}: {Bytes} bayt, {Deleted} silindi, {Failed} başarısız, {Elapsed} ms",
            runId, state.BytesFreed, state.Deleted, state.Failures.Count, stopwatch.ElapsedMilliseconds);

        return cleanResult;
    }

    private async Task CleanRuleAsync(
        RuleScanResult result,
        CleanState state,
        CleanOptions options,
        IProgress<CleanProgress>? progress,
        int total,
        CancellationToken cancellationToken)
    {
        CleaningRule rule = result.Rule;

        if (rule.Handler is "recycleBin")
        {
            CleanRecycleBin(result, state, options);
            return;
        }

        // Windows Update önbelleği için servis kısa süre durdurulur; aksi hâlde
        // dosyalar kilitli olur ve temizlik hiçbir işe yaramaz.
        await using ServiceSuspension suspension = rule.Handler is "windowsUpdateCache"
            ? await ServiceSuspension.SuspendAsync(["wuauserv", "bits"], _logger, options.DryRun, cancellationToken)
                .ConfigureAwait(false)
            : ServiceSuspension.None;

        var recycleBatch = new List<string>();

        foreach (ScanItem item in result.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CleanItem(item, rule, state, options, recycleBatch);

            progress?.Report(new CleanProgress
            {
                CurrentRule = rule.Name.Resolve(),
                Processed = state.Processed,
                Total = total,
                BytesFreed = state.BytesFreed
            });
        }

        if (recycleBatch.Count > 0 && !options.DryRun)
        {
            FlushRecycleBatch(recycleBatch, rule, state);
        }

        if (rule.RemoveEmptyDirectories && options.RemoveEmptyDirectories && !options.DryRun)
        {
            RemoveEmptyDirectories(result, state);
        }
    }

    private void CleanItem(
        ScanItem item,
        CleaningRule rule,
        CleanState state,
        CleanOptions options,
        List<string> recycleBatch)
    {
        state.Processed++;

        GuardVerdict verdict = _guard.InspectFile(item.Path, item.AllowedRoot);

        if (!verdict.IsAllowed)
        {
            state.SkippedByGuard++;
            state.Items.Add(new HistoryItem
            {
                Path = item.Path,
                RuleId = rule.Id,
                Bytes = item.Bytes,
                Outcome = HistoryItemOutcome.SkippedByGuard,
                Message = verdict.Describe()
            });

            _logger.LogDebug("Guard reddetti {Path}: {Reason}", item.Path, verdict.Reason);
            return;
        }

        if (options.DryRun)
        {
            state.BytesFreed += item.Bytes;
            state.Deleted++;
            return;
        }

        switch (rule.DeleteMode)
        {
            case DeleteMode.RecycleBin:
                recycleBatch.Add(item.Path);
                state.PendingRecycleBytes += item.Bytes;
                break;

            case DeleteMode.Quarantine:
                Quarantine(item, rule, state);
                break;

            default:
                DeletePermanently(item, rule, state, options);
                break;
        }
    }

    private void Quarantine(ScanItem item, CleaningRule rule, CleanState state)
    {
        QuarantineSession session = state.EnsureQuarantineSession();

        if (session.TryStore(item.Path, rule.Id, item.Bytes, item.LastWriteUtc, out string? error))
        {
            state.BytesFreed += item.Bytes;
            state.Quarantined++;
            state.Items.Add(new HistoryItem
            {
                Path = item.Path,
                RuleId = rule.Id,
                Bytes = item.Bytes,
                Outcome = HistoryItemOutcome.Quarantined
            });

            return;
        }

        RecordFailure(item, rule, state, error ?? "Karantinaya taşınamadı.");
    }

    private void DeletePermanently(ScanItem item, CleaningRule rule, CleanState state, CleanOptions options)
    {
        try
        {
            File.Delete(item.Path);
            RecordDeleted(item, rule, state, HistoryItemOutcome.Deleted);
        }
        catch (UnauthorizedAccessException)
        {
            // Salt-okunur bayrağı yüzünden reddedilmiş olabilir; kaldırıp bir kez daha dene.
            if (TryClearReadOnly(item.Path))
            {
                try
                {
                    File.Delete(item.Path);
                    RecordDeleted(item, rule, state, HistoryItemOutcome.Deleted);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    RecordFailure(item, rule, state, ex.Message);
                    return;
                }
            }

            RecordFailure(item, rule, state, "Erişim reddedildi.");
        }
        catch (IOException ex)
        {
            // Kilitli: yeniden başlatmada silinsin diye işaretle.
            if (options.ScheduleLockedFilesForReboot && DelayedDelete.ScheduleFileDeletion(item.Path))
            {
                state.ScheduledForReboot++;
                state.Items.Add(new HistoryItem
                {
                    Path = item.Path,
                    RuleId = rule.Id,
                    Bytes = item.Bytes,
                    Outcome = HistoryItemOutcome.ScheduledForReboot
                });

                return;
            }

            RecordFailure(item, rule, state, ex.Message);
        }
    }

    private void FlushRecycleBatch(List<string> paths, CleaningRule rule, CleanState state)
    {
        if (ShellFileOperations.DeleteToRecycleBin(paths))
        {
            state.BytesFreed += state.PendingRecycleBytes;
            state.RecycleBin += paths.Count;

            foreach (string path in paths)
            {
                state.Items.Add(new HistoryItem
                {
                    Path = path,
                    RuleId = rule.Id,
                    Bytes = 0,
                    Outcome = HistoryItemOutcome.RecycleBin
                });
            }
        }
        else
        {
            foreach (string path in paths)
            {
                state.Failures.Add(new CleanFailure(path, rule.Id, "Geri Dönüşüm Kutusu'na gönderilemedi."));
            }
        }

        state.PendingRecycleBytes = 0;
        paths.Clear();
    }

    private void CleanRecycleBin(RuleScanResult result, CleanState state, CleanOptions options)
    {
        foreach (ScanItem item in result.Items)
        {
            state.Processed++;

            if (options.DryRun)
            {
                state.BytesFreed += item.Bytes;
                state.Deleted++;
                continue;
            }

            if (RecycleBin.Empty(item.AllowedRoot))
            {
                state.BytesFreed += item.Bytes;
                state.Deleted++;
                state.Items.Add(new HistoryItem
                {
                    Path = item.Path,
                    RuleId = result.Rule.Id,
                    Bytes = item.Bytes,
                    Outcome = HistoryItemOutcome.Deleted
                });
            }
            else
            {
                state.Failures.Add(new CleanFailure(item.Path, result.Rule.Id, "Geri Dönüşüm Kutusu boşaltılamadı."));
            }
        }
    }

    /// <summary>
    /// Temizlik sonrası boşalan klasörleri kaldırır. En derinden yukarı doğru ilerler,
    /// böylece iç içe boş klasörler tek geçişte toplanır. Kuralın kökü asla silinmez.
    /// </summary>
    private void RemoveEmptyDirectories(RuleScanResult result, CleanState state)
    {
        var candidates = result.Items
            .Select(i => Path.GetDirectoryName(i.Path))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(d => d!.Length)
            .ToArray();

        var roots = result.Items
            .Select(i => PathResolver.Normalize(i.AllowedRoot))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string? directory in candidates)
        {
            string current = directory!;

            while (!string.IsNullOrEmpty(current) && !roots.Contains(PathResolver.Normalize(current)))
            {
                if (!Directory.Exists(current))
                {
                    break;
                }

                if (!_guard.InspectDirectory(current, current).IsAllowed &&
                    !roots.Any(root => PathResolver.IsUnder(current, root)))
                {
                    break;
                }

                try
                {
                    if (Directory.EnumerateFileSystemEntries(current).Any())
                    {
                        break;
                    }

                    Directory.Delete(current);
                    state.EmptyDirectoriesRemoved++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    break;
                }

                current = Path.GetDirectoryName(current) ?? string.Empty;
            }
        }
    }

    private void RecordDeleted(ScanItem item, CleaningRule rule, CleanState state, HistoryItemOutcome outcome)
    {
        state.BytesFreed += item.Bytes;
        state.Deleted++;
        state.Items.Add(new HistoryItem
        {
            Path = item.Path,
            RuleId = rule.Id,
            Bytes = item.Bytes,
            Outcome = outcome
        });
    }

    private void RecordFailure(ScanItem item, CleaningRule rule, CleanState state, string reason)
    {
        state.Failures.Add(new CleanFailure(item.Path, rule.Id, reason));
        state.Items.Add(new HistoryItem
        {
            Path = item.Path,
            RuleId = rule.Id,
            Bytes = item.Bytes,
            Outcome = HistoryItemOutcome.Failed,
            Message = reason
        });
    }

    private static bool TryClearReadOnly(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);

            if (!attributes.HasFlag(FileAttributes.ReadOnly))
            {
                return false;
            }

            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void WriteHistory(CleanResult result, IReadOnlyList<RuleScanResult> selection, CleanState state)
    {
        _history.Append(
            new HistoryRun
            {
                RunId = result.RunId,
                Operation = HistoryOperation.Clean,
                StartedAt = result.StartedAt,
                Duration = result.Duration,
                BytesFreed = result.BytesFreed,
                ItemsAffected = result.Deleted + result.Quarantined + result.SentToRecycleBin,
                ItemsFailed = result.Failures.Count,
                ItemsScheduledForReboot = result.ScheduledForReboot,
                IsReversible = result.IsReversible,
                FreeSpaceBefore = result.FreeSpaceBefore,
                FreeSpaceAfter = result.FreeSpaceAfter,
                RuleIds = selection.Select(s => s.Rule.Id).ToArray()
            },
            state.Items);
    }

    private long GetSystemDriveFreeBytes() => _systemInfo.Capture().SystemDrive?.FreeBytes ?? 0;

    /// <summary>Tek bir temizlik çalıştırmasının değişken durumu.</summary>
    private sealed class CleanState(Guid runId, CleanOptions options, QuarantineStore store)
    {
        private QuarantineSession? _session;

        public Guid RunId { get; } = runId;

        public List<CleanFailure> Failures { get; } = [];

        public List<HistoryItem> Items { get; } = [];

        public QuarantineSession? QuarantineSession => _session;

        public long BytesFreed;
        public long PendingRecycleBytes;
        public int Processed;
        public int Deleted;
        public int Quarantined;
        public int RecycleBin;
        public int ScheduledForReboot;
        public int SkippedByGuard;
        public int EmptyDirectoriesRemoved;

        public QuarantineSession EnsureQuarantineSession()
        {
            _ = options;
            return _session ??= store.BeginSession(RunId);
        }
    }
}
