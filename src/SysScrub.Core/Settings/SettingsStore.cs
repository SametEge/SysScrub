using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SysScrub.Core.Machine;

namespace SysScrub.Core.Settings;

/// <summary>
/// Ayarları diskte tutar.
///
/// Dosya insan tarafından okunabilir JSON: teknisyen elle düzenleyebilsin ve
/// bir kurulumdan diğerine taşıyabilsin. Bozuk ya da eksik dosya uygulamayı
/// düşürmüyor — varsayılanlara dönülüyor, çünkü ayar dosyası yüzünden açılmayan
/// bir bakım aracı işe yaramaz.
///
/// Yazma atomik: geçici dosyaya yazılıp yerine taşınıyor. Yazma sırasında
/// elektrik giderse eski dosya bozulmadan kalıyor.
/// </summary>
public sealed class SettingsStore
{
    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly object _writeLock = new();

    private AppSettings _current;

    public SettingsStore(string? directory = null, ILogger<SettingsStore>? logger = null)
    {
        _path = Path.Combine(directory ?? AppPaths.DataDirectory, FileName);
        _logger = logger ?? NullLogger<SettingsStore>.Instance;
        _current = Load();
    }

    // Ad bilerek "Path" değil: o isim System.IO.Path'i gölgeleyip bu dosyadaki
    // her çağrıyı bozuyor.
    public string FilePath => _path;

    public AppSettings Current => _current;

    public event EventHandler<AppSettings>? Changed;

    /// <summary>Ayarları değiştirir ve diske yazar.</summary>
    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        AppSettings updated = change(_current).Normalized();

        if (updated == _current)
        {
            return _current;
        }

        _current = updated;
        Save(updated);

        Changed?.Invoke(this, updated);

        return updated;
    }

    private AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            return AppSettings.Default;
        }

        try
        {
            string json = File.ReadAllText(_path);

            return (JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? AppSettings.Default)
                .Normalized();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Ayar dosyası okunamadı, varsayılanlara dönülüyor: {Path}", _path);

            return AppSettings.Default;
        }
    }

    private void Save(AppSettings settings)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_path);

            if (directory is { Length: > 0 })
            {
                Directory.CreateDirectory(directory);
            }

            lock (_writeLock)
            {
                string temporary = _path + ".tmp";

                File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
                File.Move(temporary, _path, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ayar yazılamadıysa oturum içinde geçerli kalır; kullanıcı durdurulmaz.
            _logger.LogWarning(ex, "Ayar dosyası yazılamadı: {Path}", _path);
        }
    }
}
