using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SysScrub.Core.Disks;

/// <summary>Tablodaki tek bir öznitelik tanımı.</summary>
public sealed record SmartAttributeDefinition
{
    [JsonPropertyName("id")]
    public required byte Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Ham değeri sıfırdan büyük olduğunda sorun anlamına gelen öznitelik.</summary>
    [JsonPropertyName("critical")]
    public bool Critical { get; init; }
}

/// <summary>
/// S.M.A.R.T. öznitelik adlarını ve açıklamalarını taşıyan tablo.
///
/// Yorumlama neden kodda değil: aynı öznitelik kimliği üreticiden üreticiye
/// farklı anlama geliyor. Tablo veri olarak durursa yeni üretici desteği
/// bir satır eklemek olur, kod değişikliği değil.
///
/// Bu ayrım aynı zamanda kullanıcıya "Reallocated Sector Count: 0x000000000000"
/// yerine "Bozuk sektör yok" diyebilmemizi sağlıyor.
/// </summary>
public sealed class SmartAttributeTable
{
    private const string ResourceName = "SysScrub.Core.Disks.SmartAttributes.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<byte, SmartAttributeDefinition> _definitions;

    public SmartAttributeTable(IEnumerable<SmartAttributeDefinition>? definitions = null)
    {
        _definitions = (definitions ?? LoadEmbedded()).ToDictionary(d => d.Id);
    }

    public int Count => _definitions.Count;

    /// <summary>Kimliğin tablo karşılığı; tabloda yoksa null.</summary>
    public SmartAttributeDefinition? Find(byte id) =>
        _definitions.TryGetValue(id, out SmartAttributeDefinition? definition) ? definition : null;

    /// <summary>Ham özniteliği kullanıcıya gösterilebilir hâle getirir.</summary>
    public SmartAttribute Describe(RawAtaAttribute raw)
    {
        SmartAttributeDefinition? definition = Find(raw.Id);

        return new SmartAttribute
        {
            Id = raw.Id,
            // Tanınmayan kimlik gizlenmiyor: üreticiye özel bir öznitelik olabilir
            // ve ham değerini görmek teknisyen için hâlâ bilgi.
            Name = definition?.Name ?? $"Üreticiye özel öznitelik (0x{raw.Id:X2})",
            Description = definition?.Description,
            Current = raw.Current,
            Worst = raw.Worst,
            Threshold = raw.Threshold,
            Raw = raw.Raw,
            IsCritical = definition?.Critical ?? false
        };
    }

    private static IEnumerable<SmartAttributeDefinition> LoadEmbedded()
    {
        Assembly assembly = typeof(SmartAttributeTable).Assembly;

        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);

        if (stream is null)
        {
            return [];
        }

        try
        {
            TableFile? file = JsonSerializer.Deserialize<TableFile>(stream, JsonOptions);

            return file?.Attributes ?? [];
        }
        catch (JsonException)
        {
            // Tablo okunamazsa öznitelikler kimlikleriyle gösterilir; modül düşmez.
            return [];
        }
    }

    private sealed record TableFile
    {
        [JsonPropertyName("attributes")]
        public IReadOnlyList<SmartAttributeDefinition> Attributes { get; init; } = [];
    }
}
