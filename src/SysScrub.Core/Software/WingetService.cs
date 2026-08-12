using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SysScrub.Core.Software;

/// <summary>
/// winget sarmalayıcısı.
///
/// winget'in "upgrade" komutunun JSON çıktısı yok; tablo sabit genişlikli sütunlarla
/// yazılıyor ve başlıklar Windows'un diline göre değişiyor. Bu yüzden ayrıştırıcı
/// başlık ADINA değil, başlık satırındaki sütun başlangıç konumlarına bakıyor —
/// böylece hangi dilde olursa olsun doğru çalışıyor.
/// </summary>
public sealed class WingetService(ILogger<WingetService>? logger = null)
{
    private static readonly TimeSpan ListTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan UpgradeTimeout = TimeSpan.FromMinutes(20);

    /// <summary>winget upgrade tablosu her zaman beş sütun: ad, kimlik, sürüm, yeni sürüm, kaynak.</summary>
    private const int ExpectedColumns = 5;

    private readonly ILogger _logger = logger ?? NullLogger<WingetService>.Instance;
    private string? _resolvedPath;

    /// <summary>Güncellenebilir programları listeler. Hiçbir şeyi değiştirmez.</summary>
    public async Task<SoftwareUpdateList> ListUpgradesAsync(CancellationToken cancellationToken = default)
    {
        string? winget = ResolveWingetPath();

        if (winget is null)
        {
            return new SoftwareUpdateList { Outcome = WingetOutcome.NotInstalled };
        }

        var stopwatch = Stopwatch.StartNew();

        ProcessResult result = await RunAsync(
            winget,
            "upgrade --include-unknown --disable-interactivity --accept-source-agreements",
            ListTimeout,
            cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        // winget güncelleme yokken de sıfır dışı kod dönebiliyor; çıktıyı yine ayrıştırıyoruz.
        IReadOnlyList<SoftwareUpdate> updates = ParseUpgradeTable(result.Output);

        if (updates.Count == 0 && !result.Succeeded && result.Output.Length == 0)
        {
            return new SoftwareUpdateList
            {
                Outcome = WingetOutcome.Failed,
                Message = result.Error,
                Duration = stopwatch.Elapsed
            };
        }

        _logger.LogInformation("winget {Count} güncellenebilir program bildirdi", updates.Count);

        return new SoftwareUpdateList
        {
            Outcome = WingetOutcome.Completed,
            Updates = updates,
            Duration = stopwatch.Elapsed
        };
    }

    /// <summary>Tek bir paketi günceller.</summary>
    public async Task<SoftwareUpgradeResult> UpgradeAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        string? winget = ResolveWingetPath();

        if (winget is null)
        {
            return new SoftwareUpgradeResult(packageId, false, "winget bulunamadı.");
        }

        ProcessResult result = await RunAsync(
            winget,
            $"upgrade --id \"{packageId}\" --exact --silent --disable-interactivity " +
            "--accept-package-agreements --accept-source-agreements",
            UpgradeTimeout,
            cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            _logger.LogInformation("{Package} güncellendi", packageId);
            return new SoftwareUpgradeResult(packageId, true, null);
        }

        // winget hata metnini standart çıkışa yazıyor; son anlamlı satır en açıklayıcısı.
        string message = LastMeaningfulLine(result.Output) ?? result.Error ?? "Bilinmeyen hata.";

        _logger.LogWarning("{Package} güncellenemedi: {Message}", packageId, message);

        return new SoftwareUpgradeResult(packageId, false, message);
    }

    /// <summary>
    /// Sabit genişlikli tabloyu ayrıştırır.
    ///
    /// Yöntem: tirelerden oluşan ayırıcı satır bulunur, bir üstü başlık satırıdır.
    /// Başlıktaki boşluk→karakter geçişleri sütun başlangıçlarını verir. Sütunlar
    /// sıraya göre okunur, başlık metni hiç kullanılmaz.
    /// </summary>
    public static IReadOnlyList<SoftwareUpdate> ParseUpgradeTable(string output)
    {
        string[] lines = output.Replace("\r", string.Empty).Split('\n');

        int separatorIndex = Array.FindIndex(lines, IsSeparatorLine);

        if (separatorIndex <= 0)
        {
            return [];
        }

        int[] columns = ColumnStarts(lines[separatorIndex - 1]);

        if (columns.Length < ExpectedColumns)
        {
            return [];
        }

        var updates = new List<SoftwareUpdate>();

        for (int i = separatorIndex + 1; i < lines.Length; i++)
        {
            string line = lines[i];

            if (string.IsNullOrWhiteSpace(line))
            {
                // Tablo bitti; sonrasında özet metinleri geliyor.
                break;
            }

            // "N upgrades available." gibi özet satırları sütun düzenine uymaz.
            if (line.Length <= columns[1])
            {
                continue;
            }

            string name = Column(line, columns, 0);
            string id = Column(line, columns, 1);
            string installed = Column(line, columns, 2);
            string available = Column(line, columns, 3);
            string source = Column(line, columns, 4);

            if (id.Length == 0 || available.Length == 0)
            {
                continue;
            }

            updates.Add(new SoftwareUpdate
            {
                Name = name.Length > 0 ? name : id,
                Id = id,
                InstalledVersion = installed.Length > 0 ? installed : "bilinmiyor",
                AvailableVersion = available,
                Source = source.Length > 0 ? source : "winget"
            });
        }

        return updates;
    }

    private static bool IsSeparatorLine(string line)
    {
        string trimmed = line.Trim();

        return trimmed.Length >= 10 && trimmed.All(c => c == '-');
    }

    private static int[] ColumnStarts(string header)
    {
        var starts = new List<int>();
        bool previousWasSpace = true;

        for (int i = 0; i < header.Length; i++)
        {
            bool isSpace = header[i] == ' ';

            if (previousWasSpace && !isSpace)
            {
                starts.Add(i);
            }

            previousWasSpace = isSpace;
        }

        return starts.ToArray();
    }

    private static string Column(string line, int[] columns, int index)
    {
        int start = columns[index];

        if (start >= line.Length)
        {
            return string.Empty;
        }

        int end = index + 1 < columns.Length ? Math.Min(columns[index + 1], line.Length) : line.Length;

        return line[start..end].Trim();
    }

    private static string? LastMeaningfulLine(string output)
    {
        string[] lines = output.Replace("\r", string.Empty).Split('\n');

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string line = lines[i].Trim();

            if (line.Length > 3 && !line.All(c => c is '-' or '\\' or '|' or '/'))
            {
                return line;
            }
        }

        return null;
    }

    /// <summary>
    /// winget'in konumu.
    ///
    /// Normalde PATH'teki uygulama takma adı yeterli. Yükseltilmiş süreçlerde bu
    /// takma ad bazı kurulumlarda çözülemiyor; o yüzden WindowsApps altındaki
    /// gerçek dosya da aranıyor.
    /// </summary>
    private string? ResolveWingetPath()
    {
        if (_resolvedPath is not null)
        {
            return _resolvedPath;
        }

        string aliasPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");

        if (File.Exists(aliasPath))
        {
            return _resolvedPath = aliasPath;
        }

        try
        {
            string windowsApps = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");

            string? packageDirectory = Directory
                .EnumerateDirectories(windowsApps, "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe")
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (packageDirectory is not null)
            {
                string candidate = Path.Combine(packageDirectory, "winget.exe");

                if (File.Exists(candidate))
                {
                    return _resolvedPath = candidate;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "WindowsApps klasörü okunamadı");
        }

        _logger.LogWarning("winget bulunamadı");
        return null;
    }

    private async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            // winget etkileşimli olmadığını buradan da anlıyor; ilerleme animasyonu yazmıyor.
            process.StartInfo.Environment["WINGET_DISABLE_INTERACTIVITY"] = "1";

            process.Start();

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var limited = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limited.CancelAfter(timeout);

            await process.WaitForExitAsync(limited.Token).ConfigureAwait(false);

            string output = await outputTask.ConfigureAwait(false);
            string error = await errorTask.ConfigureAwait(false);

            return new ProcessResult(process.ExitCode == 0, output, string.IsNullOrWhiteSpace(error) ? null : error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            _logger.LogWarning(ex, "winget çalıştırılamadı");
            return new ProcessResult(false, string.Empty, ex.Message);
        }
    }

    private readonly record struct ProcessResult(bool Succeeded, string Output, string? Error);
}
