using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace SysScrub.Core.Drivers;

/// <summary>Windows Update'in sunduğu tek bir sürücü güncellemesi.</summary>
public sealed record DriverUpdate
{
    public required string UpdateId { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public string? Manufacturer { get; init; }

    public string? Model { get; init; }

    public string? Version { get; init; }

    public DateTime? Date { get; init; }

    public string? HardwareId { get; init; }

    public long SizeBytes { get; init; }

    public bool IsDownloaded { get; init; }
}

public enum DriverSearchOutcome
{
    Completed,

    /// <summary>Grup ilkesi veya Windows ayarı sürücü güncellemelerini kapatmış.</summary>
    DisabledByPolicy,

    /// <summary>Windows Update servisine ulaşılamadı.</summary>
    Unavailable,

    Failed
}

public sealed record DriverSearchResult
{
    public required DriverSearchOutcome Outcome { get; init; }

    public IReadOnlyList<DriverUpdate> Updates { get; init; } = [];

    public string? Message { get; init; }

    public TimeSpan Duration { get; init; }

    public string Describe() => Outcome switch
    {
        DriverSearchOutcome.Completed when Updates.Count == 0 =>
            "Windows Update yeni sürücü sunmuyor. Sürücülerin güncel.",
        DriverSearchOutcome.Completed =>
            $"{Updates.Count} sürücü güncellemesi bulundu.",
        DriverSearchOutcome.DisabledByPolicy =>
            "Sürücü güncellemeleri Windows ayarlarıyla kapatılmış. Windows Update ayarlarından açabilirsin.",
        DriverSearchOutcome.Unavailable =>
            "Windows Update servisine ulaşılamadı. İnternet bağlantını kontrol et.",
        _ => Message ?? "Sürücü araması başarısız."
    };
}

/// <summary>
/// Sürücü güncellemelerini Windows Update Agent üzerinden arar.
///
/// Kaynak bilinçli olarak Microsoft'un kendi kataloğu: buradan gelen her sürücü
/// WHQL imzalı ve Microsoft tarafından o donanıma uygun bulunmuş. Üçüncü parti
/// sürücü aynası tutmak, DriverBooster tarzı uygulamaları malware sınırına iten şey.
///
/// WUA COM arayüzü geç bağlama ile kullanılıyor: interop derlemesi taşımak yerine
/// dinamik çağrı, hem bağımlılığı kaldırıyor hem de arayüzün olmadığı ortamlarda
/// düzgün hata vermeyi kolaylaştırıyor.
/// </summary>
public sealed class WindowsUpdateDriverSource(ILogger<WindowsUpdateDriverSource>? logger = null)
{
    private const string DriverSearchCriteria = "Type='Driver' and IsInstalled=0 and IsHidden=0";

    private readonly ILogger _logger = logger ?? NullLogger<WindowsUpdateDriverSource>.Instance;

    /// <summary>
    /// Sürücü güncellemesi arar.
    ///
    /// Arama çevrimiçi ve yavaş (10-60 saniye). COM çağrısı iptal edilemediği için
    /// iptal yalnızca sonuç işlenmeden önce devreye girer; çağrı arka planda tamamlanır.
    /// </summary>
    public async Task<DriverSearchResult> SearchAsync(CancellationToken cancellationToken = default)
    {
        if (IsDriverSearchDisabled())
        {
            return new DriverSearchResult { Outcome = DriverSearchOutcome.DisabledByPolicy };
        }

        DateTimeOffset started = DateTimeOffset.Now;

        try
        {
            DriverSearchResult result = await Task.Run(Search, cancellationToken).ConfigureAwait(false);

            return result with { Duration = DateTimeOffset.Now - started };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (COMException ex)
        {
            _logger.LogWarning(ex, "Windows Update sürücü araması başarısız (COM {HResult:X8})", ex.HResult);

            return new DriverSearchResult
            {
                Outcome = DriverSearchOutcome.Unavailable,
                Message = ex.Message,
                Duration = DateTimeOffset.Now - started
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Windows Update sürücü araması başarısız");

            return new DriverSearchResult
            {
                Outcome = DriverSearchOutcome.Failed,
                Message = ex.Message,
                Duration = DateTimeOffset.Now - started
            };
        }
    }

    private DriverSearchResult Search()
    {
        Type? sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");

        if (sessionType is null)
        {
            return new DriverSearchResult
            {
                Outcome = DriverSearchOutcome.Unavailable,
                Message = "Windows Update Agent bu sistemde bulunamadı."
            };
        }

        dynamic? session = Activator.CreateInstance(sessionType);

        if (session is null)
        {
            return new DriverSearchResult { Outcome = DriverSearchOutcome.Unavailable };
        }

        try
        {
            session.ClientApplicationID = "SysScrub";

            dynamic searcher = session.CreateUpdateSearcher();
            searcher.Online = true;

            dynamic searchResult = searcher.Search(DriverSearchCriteria);
            dynamic updates = searchResult.Updates;

            var found = new List<DriverUpdate>();
            int count = updates.Count;

            for (int i = 0; i < count; i++)
            {
                dynamic update = updates.Item(i);

                found.Add(new DriverUpdate
                {
                    UpdateId = SafeString(() => update.Identity.UpdateID) ?? Guid.NewGuid().ToString(),
                    Title = SafeString(() => update.Title) ?? "Bilinmeyen sürücü",
                    Description = SafeString(() => update.Description),
                    Manufacturer = SafeString(() => update.DriverManufacturer),
                    Model = SafeString(() => update.DriverModel),
                    Version = SafeString(() => update.DriverVerDate is null ? null : update.DriverModel),
                    Date = SafeDate(() => update.DriverVerDate),
                    HardwareId = SafeString(() => update.DriverHardwareID),
                    SizeBytes = SafeLong(() => update.MaxDownloadSize),
                    IsDownloaded = SafeBool(() => update.IsDownloaded)
                });
            }

            _logger.LogInformation("Windows Update {Count} sürücü güncellemesi sundu", found.Count);

            return new DriverSearchResult { Outcome = DriverSearchOutcome.Completed, Updates = found };
        }
        finally
        {
            if (Marshal.IsComObject(session))
            {
                Marshal.FinalReleaseComObject(session);
            }
        }
    }

    /// <summary>
    /// Windows'ta sürücü güncellemeleri kapatılmış mı.
    ///
    /// Kullanıcı "cihaz üreticisinin uygulamalarını indirme" ayarını kapattıysa ya da
    /// grup ilkesi sürücüleri hariç tuttuysa arama boş döner. Bunu "sürücülerin güncel"
    /// diye göstermek yanıltıcı olur; ayırt edip söylüyoruz.
    /// </summary>
    private bool IsDriverSearchDisabled()
    {
        try
        {
            using RegistryKey? policy = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate");

            if (policy?.GetValue("ExcludeWUDriversInQualityUpdate") is int excluded && excluded == 1)
            {
                return true;
            }

            using RegistryKey? searching = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching");

            // SearchOrderConfig = 0 → "Windows Update'ten sürücü arama" kapalı.
            return searching?.GetValue("SearchOrderConfig") is int order && order == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static string? SafeString(Func<object?> getter)
    {
        try
        {
            string? value = getter()?.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return null;
        }
    }

    private static DateTime? SafeDate(Func<object?> getter)
    {
        try
        {
            return getter() is DateTime value ? value : null;
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return null;
        }
    }

    private static long SafeLong(Func<object?> getter)
    {
        try
        {
            return getter() is { } value ? Convert.ToInt64(value) : 0;
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException
                                   or FormatException or InvalidCastException or OverflowException)
        {
            return 0;
        }
    }

    private static bool SafeBool(Func<object?> getter)
    {
        try
        {
            return getter() is bool value && value;
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return false;
        }
    }
}
