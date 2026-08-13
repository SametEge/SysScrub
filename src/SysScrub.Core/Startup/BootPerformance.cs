using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SysScrub.Core.Startup;

/// <summary>
/// Açılış gecikmelerini Windows'un kendi ölçümünden okur.
///
/// Rakiplerin "yüksek etki / düşük etki" etiketleri genelde tahmin. Windows ise
/// her açılışta hangi uygulamanın ne kadar geciktirdiğini Tanılama-Performans
/// günlüğüne yazıyor (olay 101). Biz o gerçek sayıyı kullanıyoruz.
///
/// Günlük okunamazsa (kapalı ya da yetki yok) etki sütunu boş kalır; uydurma
/// bir değer üretilmez.
/// </summary>
public sealed class BootPerformance(ILogger<BootPerformance>? logger = null)
{
    private const string LogName = "Microsoft-Windows-Diagnostics-Performance/Operational";

    /// <summary>Uygulama başlangıç gecikmesi olayı.</summary>
    private const int ApplicationDelayEventId = 101;

    /// <summary>Kaç açılışa kadar geriye bakılacağı.</summary>
    private const int MaxEvents = 200;

    private readonly ILogger _logger = logger ?? NullLogger<BootPerformance>.Instance;

    /// <summary>
    /// Uygulama adına göre ortalama açılış gecikmesi (milisaniye).
    /// Anahtar, olayda geçen dosya adı ("uygulama.exe").
    /// </summary>
    public Task<IReadOnlyDictionary<string, int>> LoadDelaysAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyDictionary<string, int>>(() => Load(cancellationToken), cancellationToken);

    private IReadOnlyDictionary<string, int> Load(CancellationToken cancellationToken)
    {
        var samples = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var query = new EventLogQuery(LogName, PathType.LogName, $"*[System/EventID={ApplicationDelayEventId}]")
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);

            for (int i = 0; i < MaxEvents; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using EventRecord? record = reader.ReadEvent();

                if (record is null)
                {
                    break;
                }

                if (TryParse(record, out string name, out int totalTime))
                {
                    if (!samples.TryGetValue(name, out List<int>? list))
                    {
                        samples[name] = list = [];
                    }

                    list.Add(totalTime);
                }
            }
        }
        catch (EventLogNotFoundException)
        {
            _logger.LogInformation("Tanılama-Performans günlüğü bulunamadı; açılış ölçümü yok");
            return new Dictionary<string, int>();
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogInformation("Açılış ölçümü için yönetici hakkı gerekiyor");
            return new Dictionary<string, int>();
        }
        catch (EventLogException ex)
        {
            _logger.LogWarning(ex, "Açılış ölçümü okunamadı");
            return new Dictionary<string, int>();
        }

        // Tek bir yavaş açılış temsil etmiyor; ortalama alınıyor.
        return samples.ToDictionary(
            pair => pair.Key,
            pair => (int)pair.Value.Average(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Olay verisi XML olarak geliyor: Name (dosya adı) ve TotalTime (ms) alanları.
    /// Yerelleştirilmiş metne değil, alan adlarına bakıyoruz.
    /// </summary>
    private static bool TryParse(EventRecord record, out string name, out int totalTime)
    {
        name = string.Empty;
        totalTime = 0;

        try
        {
            var xml = XDocument.Parse(record.ToXml());
            XNamespace ns = xml.Root?.Name.Namespace ?? XNamespace.None;

            foreach (XElement data in xml.Descendants(ns + "Data"))
            {
                string? attribute = data.Attribute("Name")?.Value;

                if (string.Equals(attribute, "Name", StringComparison.Ordinal))
                {
                    name = data.Value.Trim();
                }
                else if (string.Equals(attribute, "TotalTime", StringComparison.Ordinal))
                {
                    int.TryParse(data.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out totalTime);
                }
            }

            return name.Length > 0 && totalTime > 0;
        }
        catch (Exception ex) when (ex is EventLogException or System.Xml.XmlException)
        {
            return false;
        }
    }
}
