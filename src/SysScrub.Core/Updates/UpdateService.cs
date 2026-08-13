using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.Extensions.Logging;
using SysScrub.Core.Formatting;
using SysScrub.Core.Machine;

namespace SysScrub.Core.Updates;

/// <summary>Güncelleme denetiminin sonucu.</summary>
public enum UpdateStatus
{
    /// <summary>Henüz bakılmadı.</summary>
    Unknown,

    UpToDate,

    Available,

    /// <summary>Yeni sürüm var ama kurulum paketi eklenmemiş; yalnızca sayfası açılabilir.</summary>
    AvailableWithoutSetup,

    /// <summary>Ağ yok, hız sınırı doldu ya da yanıt bozuk. Neden metinde.</summary>
    Failed
}

/// <summary>Denetim çıktısı. Başarısızlıkta <see cref="Message"/> nedeni taşır.</summary>
public sealed record UpdateCheckResult(UpdateStatus Status, GitHubRelease? Release = null, string Message = "")
{
    public static UpdateCheckResult UpToDate { get; } = new(UpdateStatus.UpToDate);
}

/// <summary>İndirme ilerlemesi; toplam boyut bilinmiyorsa <see cref="Total"/> sıfırdır.</summary>
public readonly record struct DownloadProgress(long Received, long Total)
{
    public double? Fraction => Total > 0 ? Math.Clamp((double)Received / Total, 0, 1) : null;
}

/// <summary>Bütünlük doğrulamasının sonucu.</summary>
public enum ChecksumVerdict
{
    Verified,

    /// <summary>Yayında SHA256SUMS.txt yok ya da indirilemedi.</summary>
    NotPublished,

    Mismatch
}

/// <summary>İndirilmiş ve doğrulanmış kurulum paketi.</summary>
public sealed record DownloadedUpdate(GitHubRelease Release, string FilePath, ChecksumVerdict Verdict);

/// <summary>
/// Uygulamanın kendi güncellemesi.
///
/// Dağıtım kanalımız GitHub Releases olduğu için güncelleme de oradan geliyor:
/// yayınlar listelenir, sürüm karşılaştırılır, kurulum paketi indirilir,
/// yayınla birlikte gelen SHA256 listesiyle doğrulanır ve ancak ondan sonra
/// çalıştırılır. Özet tutmuyorsa dosya silinir — bozuk ya da değiştirilmiş bir
/// kurulumu çalıştırmaktansa güncellememek yeğdir.
/// </summary>
public sealed class UpdateService
{
    /// <summary>Yayınların okunduğu depo. Tek yerde dursun diye burada.</summary>
    public const string Repository = "SametEge/SysScrub";

    public const string ReleasesPageUrl = $"https://github.com/{Repository}/releases/latest";

    private const string ApiUrl = $"https://api.github.com/repos/{Repository}/releases?per_page=20";

    /// <summary>GitHub kimliksiz isteklerde de User-Agent zorunlu tutuyor.</summary>
    private static readonly ProductInfoHeaderValue Agent = new("SysScrub", CurrentVersionText());

    private readonly HttpClient _http;
    private readonly ILogger<UpdateService> _logger;

    public UpdateService(HttpClient http, ILogger<UpdateService> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Çalışan derlemenin sürümü.</summary>
    public static AppVersion Current { get; } = AppVersion.FromAssembly(typeof(UpdateService).Assembly);

    /// <summary>İndirilen paketlerin durduğu klasör.</summary>
    public static string DownloadDirectory => Path.Combine(AppPaths.DataDirectory, "updates");

    /// <summary>
    /// Portatif kurulumda setup çalıştırmıyoruz: kullanıcı bilerek kuruluma
    /// karşı bir dağıtım seçmiş, onun klasörünü Program Files'a taşımak olmaz.
    /// Yeni sürüm yine bildirilir, indirmesi kendisine bırakılır.
    /// </summary>
    public static bool CanInstallInPlace => !AppPaths.IsPortable;

    // ------------------------------------------------------------------ denetim

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            request.Headers.UserAgent.Add(Agent);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(UpdateStatus.Failed, null, DescribeFailure(response));
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return Evaluate(GitHubReleaseParser.ParseList(json), Current);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or IOException)
        {
            _logger.LogWarning(ex, "Güncelleme denetimi başarısız");

            return new UpdateCheckResult(UpdateStatus.Failed, null, ex.Message);
        }
    }

    /// <summary>
    /// Listeden çalışan sürümden yenisini seçer.
    ///
    /// Ön yayın kuralı kendiliğinden ayarlanıyor: alfa çalıştıran kişiye ön
    /// yayınlar da önerilir, kararlı sürüm çalıştırana yalnızca kararlı sürüm.
    /// Böylece kimse ne beta'ya sürüklenir ne de alfa'da mahsur kalır.
    /// </summary>
    public static UpdateCheckResult Evaluate(
        IReadOnlyList<GitHubRelease> releases,
        AppVersion current,
        bool? includePreRelease = null)
    {
        bool preReleasesWanted = includePreRelease ?? current.IsPreRelease;

        GitHubRelease? newest = releases
            .Where(release => preReleasesWanted || !release.IsPreRelease)
            .Where(release => release.Version > current)
            .OrderByDescending(release => release.Version)
            .FirstOrDefault();

        if (newest is null)
        {
            return UpdateCheckResult.UpToDate;
        }

        return newest.Setup is null
            ? new UpdateCheckResult(UpdateStatus.AvailableWithoutSetup, newest)
            : new UpdateCheckResult(UpdateStatus.Available, newest);
    }

    // ------------------------------------------------------------------ indirme

    public async Task<DownloadedUpdate> DownloadAsync(
        GitHubRelease release,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ReleaseAsset setup = release.Setup
            ?? throw new InvalidOperationException("Bu yayında kurulum paketi yok.");

        Directory.CreateDirectory(DownloadDirectory);

        string target = Path.Combine(DownloadDirectory, setup.Name);

        await DownloadFileAsync(setup, target, progress, cancellationToken).ConfigureAwait(false);

        ChecksumVerdict verdict = await VerifyAsync(release, setup.Name, target, cancellationToken)
            .ConfigureAwait(false);

        if (verdict == ChecksumVerdict.Mismatch)
        {
            TryDelete(target);

            throw new InvalidDataException(
                $"İndirilen dosyanın SHA256 özeti yayındaki değerle uyuşmuyor: {setup.Name}");
        }

        return new DownloadedUpdate(release, target, verdict);
    }

    private async Task DownloadFileAsync(
        ReleaseAsset asset,
        string target,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Yarım kalmış indirme tam paket sanılmasın diye önce geçici ada yazılıyor.
        string temporary = target + ".part";

        TryDelete(temporary);

        using HttpRequestMessage request = new(HttpMethod.Get, asset.DownloadUrl);
        request.Headers.UserAgent.Add(Agent);

        using HttpResponseMessage response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? asset.Size;

        await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (FileStream destination = File.Create(temporary))
        {
            byte[] buffer = new byte[81920];
            long received = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

                received += read;
                progress?.Report(new DownloadProgress(received, total));
            }
        }

        TryDelete(target);
        File.Move(temporary, target);
    }

    private async Task<ChecksumVerdict> VerifyAsync(
        GitHubRelease release,
        string fileName,
        string path,
        CancellationToken cancellationToken)
    {
        if (release.Checksums is not { } checksums)
        {
            return ChecksumVerdict.NotPublished;
        }

        string? expected;

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, checksums.DownloadUrl);
            request.Headers.UserAgent.Add(Agent);

            using HttpResponseMessage response = await _http
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            expected = ChecksumList.Parse(content).Find(fileName);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            _logger.LogWarning(ex, "SHA256 listesi indirilemedi");

            return ChecksumVerdict.NotPublished;
        }

        if (expected is null)
        {
            return ChecksumVerdict.NotPublished;
        }

        string actual = await ChecksumList.ComputeAsync(path, cancellationToken).ConfigureAwait(false);

        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
            ? ChecksumVerdict.Verified
            : ChecksumVerdict.Mismatch;
    }

    // ------------------------------------------------------------------ kurulum

    /// <summary>
    /// Kurulumu sessiz kipte başlatır ve <c>false</c> dönerse hiçbir şey olmamıştır.
    ///
    /// /RELAUNCH kendi eklediğimiz bayrak: kurulum bittiğinde uygulamayı yeniden
    /// açması için. Çağıran taraf hemen kapanmalı, yoksa kendi dosyalarını kilitler.
    /// </summary>
    public bool StartInstaller(DownloadedUpdate update)
    {
        if (!CanInstallInPlace)
        {
            return false;
        }

        if (update.Verdict == ChecksumVerdict.Mismatch || !File.Exists(update.FilePath))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = update.FilePath,
                Arguments = "/SILENT /SP- /NORESTART /CLOSEAPPLICATIONS /RELAUNCH",
                UseShellExecute = true,
                Verb = "runas"
            });

            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger.LogError(ex, "Kurulum başlatılamadı: {Path}", update.FilePath);

            return false;
        }
    }

    /// <summary>Yayın sayfasını tarayıcıda açar; indirme elle yapılacaksa tek çıkış yolu.</summary>
    public void OpenReleasePage(GitHubRelease? release = null)
    {
        string url = string.IsNullOrEmpty(release?.PageUrl) ? ReleasesPageUrl : release.PageUrl;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Yayın sayfası açılamadı");
        }
    }

    /// <summary>Kurulum tamamlandıktan sonra kalan paketleri siler.</summary>
    public void CleanDownloads()
    {
        if (!Directory.Exists(DownloadDirectory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(DownloadDirectory))
        {
            TryDelete(file);
        }
    }

    // ------------------------------------------------------------------ yardımcılar

    private static string DescribeFailure(HttpResponseMessage response)
    {
        // Kimliksiz istekler saatte 60 ile sınırlı; sebebini yazmak "bilinmeyen
        // hata" demekten iyi.
        if (response.StatusCode == HttpStatusCode.Forbidden &&
            response.Headers.TryGetValues("x-ratelimit-remaining", out IEnumerable<string>? remaining) &&
            remaining.FirstOrDefault() == "0")
        {
            return CoreText.Get("Up_RateLimit", "GitHub istek sınırı doldu.");
        }

        return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
    }

    private static string CurrentVersionText() =>
        AppVersion.FromAssembly(typeof(UpdateService).Assembly).ToString();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Silinemeyen artık dosya güncellemeyi engellemez.
        }
    }
}
