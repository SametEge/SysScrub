using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SysScrub.Core.RegistryCleaning.Scanners;

namespace SysScrub.Core.RegistryCleaning;

/// <summary>Taramanın kapsamı.</summary>
public sealed record RegistryScanOptions
{
    public IReadOnlySet<string>? EnabledScannerIds { get; init; }

    public bool IsElevated { get; init; } = true;

    public bool IsEnabled(IRegistryScanner scanner) =>
        EnabledScannerIds is null ? scanner.DefaultEnabled : EnabledScannerIds.Contains(scanner.Id);
}

/// <summary>
/// Registry taraması. Salt-okunur; hiçbir şey değiştirmez.
///
/// Tarayıcıların ürettiği her bulgu, motor tarafından RegistryGuard'dan geçirilir.
/// Tarayıcılar kendi kapsamlarını doğru bildiği varsayılmaz — bir tarayıcıdaki hata
/// güvenlik sınırını aşamamalı.
/// </summary>
public sealed class RegistryScanEngine
{
    private readonly IRegistryScanner[] _scanners;
    private readonly RegistryGuard _guard;
    private readonly ILogger _logger;

    public RegistryScanEngine(RegistryGuard guard, ILogger<RegistryScanEngine>? logger = null)
    {
        _guard = guard;
        _logger = logger ?? NullLogger<RegistryScanEngine>.Instance;
        _scanners = CreateDefaultScanners();
    }

    public IReadOnlyList<IRegistryScanner> Scanners => _scanners;

    /// <summary>Uygulamayla gelen 12 tarayıcı.</summary>
    public static IRegistryScanner[] CreateDefaultScanners() =>
    [
        new SharedDllScanner(),
        new AppPathScanner(),
        new StartupEntryScanner(),
        new MuiCacheScanner(),
        new InstallerFolderScanner(),
        new ShellExtensionScanner(),
        new FileExtensionScanner(),
        new ProgIdClassScanner(),
        new ComServerScanner(),
        new TypeLibraryScanner(),
        new UninstallEntryScanner(),
        new SoundEventScanner()
    ];

    public async Task<RegistryScanReport> ScanAsync(
        RegistryScanOptions options,
        IProgress<RegistryScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();

        IRegistryScanner[] selected = _scanners
            .Where(options.IsEnabled)
            .Where(s => options.IsElevated || !s.RequiresAdmin)
            .ToArray();

        var results = new RegistryScannerResult[selected.Length];
        int completed = 0;
        int findingsSoFar = 0;

        // Registry okuma karşılaştırmalı olarak yavaş; tarayıcılar paralel çalışıyor.
        await Task.Run(
            () => Parallel.For(
                0,
                selected.Length,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                    CancellationToken = cancellationToken
                },
                index =>
                {
                    IRegistryScanner scanner = selected[index];
                    RegistryFinding[] findings = RunScanner(scanner, cancellationToken);

                    results[index] = new RegistryScannerResult { Scanner = scanner, Findings = findings };

                    Interlocked.Add(ref findingsSoFar, findings.Length);
                    int done = Interlocked.Increment(ref completed);

                    progress?.Report(new RegistryScanProgress
                    {
                        CurrentScanner = scanner.Title,
                        Completed = done,
                        Total = selected.Length,
                        FindingsSoFar = Volatile.Read(ref findingsSoFar)
                    });
                }),
            cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        _logger.LogInformation(
            "Registry taraması bitti: {Scanners} tarayıcı, {Findings} bulgu, {Elapsed} ms",
            selected.Length, findingsSoFar, stopwatch.ElapsedMilliseconds);

        return new RegistryScanReport
        {
            Results = results.Where(r => r is not null).ToArray(),
            Duration = stopwatch.Elapsed
        };
    }

    private RegistryFinding[] RunScanner(IRegistryScanner scanner, CancellationToken cancellationToken)
    {
        var accepted = new List<RegistryFinding>();

        // Aynı kaydın iki kez listelenmemesi için güvenlik ağı: yönlendirilmeyen bir yol
        // iki görünümden de okunursa aynı fiziksel anahtar iki bulgu üretirdi.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (RegistryFinding finding in scanner.Scan(cancellationToken))
            {
                RegistryVerdict verdict = _guard.Inspect(finding.Location);

                if (verdict.IsAllowed)
                {
                    string identity =
                        $"{finding.Location.Hive}|{finding.Location.KeyPath}|{finding.Location.ValueName}|{finding.Target}";

                    if (seen.Add(identity))
                    {
                        accepted.Add(finding);
                    }

                    continue;
                }

                // Tarayıcı kapsam dışına çıktıysa bu bir hata; sessizce yutmuyoruz.
                _logger.LogWarning(
                    "{Scanner} kapsam dışı bulgu üretti ve reddedildi: {Path} ({Reason})",
                    scanner.Id, finding.Location.DisplayPath, verdict.Reason);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Tek bir tarayıcının hatası tüm taramayı düşürmemeli.
            _logger.LogError(ex, "{Scanner} tarayıcısı hata verdi", scanner.Id);
        }

        return accepted.ToArray();
    }
}
