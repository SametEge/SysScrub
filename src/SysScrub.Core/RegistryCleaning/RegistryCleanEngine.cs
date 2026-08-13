using System.Diagnostics;
using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SysScrub.Core.Cleaning;
using SysScrub.Core.Machine;
using SysScrub.Core.Formatting;

namespace SysScrub.Core.RegistryCleaning;

public sealed record RegistryCleanOptions
{
    /// <summary>Hiçbir şey silmeden ne olacağını hesaplar.</summary>
    public bool DryRun { get; init; }

    /// <summary>İşlem öncesi sistem geri yükleme noktası oluşturulsun mu.</summary>
    public bool CreateRestorePoint { get; init; } = true;
}

public sealed record RegistryCleanResult
{
    public required Guid RunId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required TimeSpan Duration { get; init; }

    public required int Removed { get; init; }

    public required int SkippedByGuard { get; init; }

    public required IReadOnlyList<string> Failures { get; init; }

    /// <summary>Geri yüklemede kullanılacak .reg dosyası.</summary>
    public string? BackupPath { get; init; }

    public RestorePointResult? RestorePoint { get; init; }

    public bool WasDryRun { get; init; }

    public bool IsReversible => !WasDryRun && BackupPath is not null && Removed > 0;
}

public sealed record RegistryCleanProgress(string CurrentScanner, int Processed, int Total)
{
    public double Fraction => Total == 0 ? 0d : (double)Processed / Total;
}

/// <summary>
/// Registry silme motoru.
///
/// Sıra bilerek böyle: önce yedek, sonra geri yükleme noktası, en son silme.
/// Yedek alınamayan hiçbir kayıt silinmez — "geri alınabilir" sözü ancak
/// yedek diskteyse geçerlidir.
/// </summary>
public sealed class RegistryCleanEngine
{
    private readonly RegistryGuard _guard;
    private readonly HistoryStore _history;
    private readonly SystemRestorePoint _restorePoint;
    private readonly ILogger _logger;

    public RegistryCleanEngine(
        RegistryGuard guard,
        HistoryStore history,
        SystemRestorePoint restorePoint,
        ILogger<RegistryCleanEngine>? logger = null)
    {
        _guard = guard;
        _history = history;
        _restorePoint = restorePoint;
        _logger = logger ?? NullLogger<RegistryCleanEngine>.Instance;
    }

    public async Task<RegistryCleanResult> CleanAsync(
        IReadOnlyList<RegistryFinding> findings,
        RegistryCleanOptions options,
        IProgress<RegistryCleanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(options);

        var runId = Guid.NewGuid();
        DateTimeOffset startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

        var failures = new List<string>();
        var items = new List<HistoryItem>();
        int removed = 0;
        int skipped = 0;
        string? backupPath = null;
        RestorePointResult? restorePoint = null;

        // Guard denetimi silmeden hemen önce tekrarlanır: tarama ile silme arasında
        // seçim değişmiş ya da bulgu elle düzenlenmiş olabilir.
        RegistryFinding[] approved = findings
            .Where(f =>
            {
                bool allowed = _guard.Inspect(f.Location).IsAllowed;

                if (!allowed)
                {
                    skipped++;
                }

                return allowed;
            })
            .ToArray();

        if (approved.Length > 0 && !options.DryRun)
        {
            backupPath = Path.Combine(
                AppPaths.BackupsDirectory,
                "registry",
                $"{startedAt:yyyyMMdd-HHmmss}-{runId:N}.reg");

            try
            {
                RegExportWriter.Write(backupPath, approved.Select(f => f.Location));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Yedek alınamadıysa hiçbir şey silinmez.
                _logger.LogError(ex, "Registry yedeği yazılamadı, temizlik iptal edildi");

                return new RegistryCleanResult
                {
                    RunId = runId,
                    StartedAt = startedAt,
                    Duration = stopwatch.Elapsed,
                    Removed = 0,
                    SkippedByGuard = skipped,
                    Failures = [CoreText.Format("Rc_E_NoBackup", "Yedek alınamadı, hiçbir kayıt silinmedi: {0}", ex.Message)]
                };
            }

            if (options.CreateRestorePoint)
            {
                restorePoint = _restorePoint.TryCreate("SysScrub registry temizliği öncesi");
            }
        }

        await Task.Run(
            () =>
            {
                int processed = 0;

                foreach (RegistryFinding finding in approved)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    processed++;

                    if (!options.DryRun && !TryDelete(finding.Location, out string? error))
                    {
                        failures.Add($"{finding.Location.DisplayPath}: {error}");

                        items.Add(new HistoryItem
                        {
                            Path = finding.Location.DisplayPath,
                            RuleId = finding.ScannerId,
                            Bytes = 0,
                            Outcome = HistoryItemOutcome.Failed,
                            Message = error
                        });

                        continue;
                    }

                    removed++;

                    items.Add(new HistoryItem
                    {
                        Path = finding.Location.DisplayPath,
                        RuleId = finding.ScannerId,
                        Bytes = 0,
                        Outcome = HistoryItemOutcome.Deleted,
                        Message = finding.Reason
                    });

                    progress?.Report(new RegistryCleanProgress(finding.ScannerId, processed, approved.Length));
                }
            },
            cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        var result = new RegistryCleanResult
        {
            RunId = runId,
            StartedAt = startedAt,
            Duration = stopwatch.Elapsed,
            Removed = removed,
            SkippedByGuard = skipped,
            Failures = failures,
            BackupPath = backupPath,
            RestorePoint = restorePoint,
            WasDryRun = options.DryRun
        };

        if (!options.DryRun)
        {
            _history.Append(
                new HistoryRun
                {
                    RunId = runId,
                    Operation = HistoryOperation.RegistryClean,
                    StartedAt = startedAt,
                    Duration = stopwatch.Elapsed,
                    BytesFreed = 0,
                    ItemsAffected = removed,
                    ItemsFailed = failures.Count,
                    IsReversible = result.IsReversible,
                    BackupPath = backupPath,
                    RuleIds = approved.Select(f => f.ScannerId).Distinct().ToArray()
                },
                items);
        }

        _logger.LogInformation(
            "Registry temizliği bitti {RunId}: {Removed} kayıt silindi, {Failed} başarısız",
            runId, removed, failures.Count);

        return result;
    }

    /// <summary>Yedekten geri yükler. reg.exe kullanılıyor: import tarafı yıllardır sınanmış.</summary>
    public static bool Restore(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"import \"{backupPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(60_000);
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private bool TryDelete(RegistryLocation location, out string? error)
    {
        error = null;

        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(location.Hive, location.View);

            if (location.TargetsWholeKey)
            {
                baseKey.DeleteSubKeyTree(location.KeyPath, throwOnMissingSubKey: false);
                return true;
            }

            using RegistryKey? key = baseKey.OpenSubKey(location.KeyPath, writable: true);

            if (key is null)
            {
                // Anahtar aradan kaybolmuş; silinecek bir şey yok, hata da değil.
                return true;
            }

            key.DeleteValue(location.ValueName!, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            error = CoreText.Get("Rc_E_Denied", "Erişim reddedildi (yönetici hakkı gerekebilir).");
            return false;
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
