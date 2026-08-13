using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SysScrub.Core.Machine;
using SysScrub.Core.Formatting;

namespace SysScrub.Core.Drivers;

public sealed record DriverBackupResult(bool Succeeded, string? Path, int PackageCount, string? Message)
{
    public string Describe() => Succeeded
        ? CoreText.Format("Dr_B_Done", "{0} sürücü paketi yedeklendi.", PackageCount)
        : Message ?? CoreText.Get("Dr_B_Failed", "Yedekleme başarısız.");
}

/// <summary>DriverStore'daki bir sürücü paketi.</summary>
public sealed record DriverPackage
{
    /// <summary>Yayınlanmış ad: oem12.inf</summary>
    public required string PublishedName { get; init; }

    public string? OriginalName { get; init; }

    public string? Provider { get; init; }

    public string? ClassName { get; init; }

    public string? Version { get; init; }

    public DateTime? Date { get; init; }

    /// <summary>Şu an bir cihaz tarafından kullanılıyor mu. Kullanımdakiler asla silinmez.</summary>
    public bool IsInUse { get; init; }
}

/// <summary>
/// Sürücü yedekleme ve DriverStore envanteri.
///
/// pnputil kullanılıyor: Windows'un kendi aracı, imzalı sürücü paketlerini
/// eksiksiz dışa aktarıyor ve geri yüklemede aynı araç işe yarıyor. Kendi
/// kopyalama mantığımızı yazmak, katalog dosyalarını ve imzaları kaçırma riski taşır.
/// </summary>
public sealed class DriverBackup(ILogger<DriverBackup>? logger = null)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private readonly ILogger _logger = logger ?? NullLogger<DriverBackup>.Instance;

    /// <summary>Tüm üçüncü parti sürücüleri tarih damgalı bir klasöre yedekler.</summary>
    public async Task<DriverBackupResult> ExportAllAsync(CancellationToken cancellationToken = default)
    {
        string directory = Path.Combine(
            AppPaths.BackupsDirectory,
            "drivers",
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DriverBackupResult(false, null, 0, CoreText.Format("Dr_B_NoFolder", "Yedek klasörü oluşturulamadı: {0}", ex.Message));
        }

        ProcessResult result = await RunPnpUtilAsync(
            $"/export-driver * \"{directory}\"", cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return new DriverBackupResult(false, directory, 0, result.Error ?? CoreText.Get("Dr_B_PnpFailed", "pnputil hata verdi."));
        }

        int count = CountExportedPackages(directory);

        _logger.LogInformation("Sürücü yedeği alındı: {Count} paket, {Path}", count, directory);

        return new DriverBackupResult(true, directory, count, null);
    }

    /// <summary>DriverStore'daki üçüncü parti sürücü paketleri.</summary>
    public async Task<IReadOnlyList<DriverPackage>> ListPackagesAsync(CancellationToken cancellationToken = default)
    {
        ProcessResult result = await RunPnpUtilAsync("/enum-drivers", cancellationToken).ConfigureAwait(false);

        return result.Succeeded ? ParsePackages(result.Output) : [];
    }

    private static int CountExportedPackages(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.inf", SearchOption.AllDirectories).Count();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// pnputil çıktısı yerelleştirilmiş: alan adları Windows'un diline göre değişiyor.
    /// Bu yüzden etiket adı yerine satır sırası ve boş satır ayırıcıları kullanılıyor.
    /// </summary>
    private static IReadOnlyList<DriverPackage> ParsePackages(string output)
    {
        var packages = new List<DriverPackage>();
        var current = new Dictionary<int, string>();
        int fieldIndex = 0;

        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.Trim();

            if (line.Length == 0)
            {
                if (current.Count > 0)
                {
                    packages.Add(BuildPackage(current));
                    current.Clear();
                    fieldIndex = 0;
                }

                continue;
            }

            int separator = line.IndexOf(':');

            if (separator <= 0)
            {
                continue;
            }

            current[fieldIndex++] = line[(separator + 1)..].Trim();
        }

        if (current.Count > 0)
        {
            packages.Add(BuildPackage(current));
        }

        return packages;
    }

    private static DriverPackage BuildPackage(Dictionary<int, string> fields)
    {
        // pnputil /enum-drivers alan sırası: yayınlanmış ad, özgün ad, sağlayıcı,
        // sınıf adı, sınıf GUID'i, sürüm+tarih, imzalayan, kullanımda.
        string published = fields.GetValueOrDefault(0, string.Empty);
        string versionAndDate = fields.GetValueOrDefault(5, string.Empty);

        (DateTime? date, string? version) = SplitVersionAndDate(versionAndDate);

        return new DriverPackage
        {
            PublishedName = published,
            OriginalName = fields.GetValueOrDefault(1),
            Provider = fields.GetValueOrDefault(2),
            ClassName = fields.GetValueOrDefault(3),
            Version = version,
            Date = date,
            IsInUse = fields.GetValueOrDefault(7, string.Empty)
                .Contains("Yes", StringComparison.OrdinalIgnoreCase) ||
                      fields.GetValueOrDefault(7, string.Empty)
                .Contains("Evet", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static (DateTime? Date, string? Version) SplitVersionAndDate(string value)
    {
        // "15.07.2024 31.0.15.4633" biçiminde geliyor.
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            return (null, value.Length > 0 ? value : null);
        }

        DateTime? date = DateTime.TryParse(parts[0], out DateTime parsed) ? parsed : null;

        return (date, parts[^1]);
    }

    private async Task<ProcessResult> RunPnpUtilAsync(string arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pnputil.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.Start();

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            string output = await outputTask.ConfigureAwait(false);
            string error = await errorTask.ConfigureAwait(false);

            return process.ExitCode == 0
                ? new ProcessResult(true, output, null)
                : new ProcessResult(false, output, string.IsNullOrWhiteSpace(error) ? output : error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            _logger.LogWarning(ex, "pnputil çalıştırılamadı: {Arguments}", arguments);
            return new ProcessResult(false, string.Empty, ex.Message);
        }
    }

    private readonly record struct ProcessResult(bool Succeeded, string Output, string? Error);
}
