using System.Text.Json;
using System.Text.Json.Serialization;

namespace SysScrub.Core.Rules;

/// <summary>Kural dosyasının kök nesnesi.</summary>
internal sealed class RuleDocument
{
    public int Version { get; set; } = 1;

    public List<RuleDefinition> Rules { get; set; } = [];
}

/// <summary>
/// JSON'daki ham kural. Doğrulanmamış hâli; RuleLoader bunu CleaningRule'a çevirirken
/// denetler. Ayrı tutulmasının sebebi, bozuk bir alanın tüm kural setini düşürmemesi.
/// </summary>
internal sealed class RuleDefinition
{
    public string? Id { get; set; }

    public string? Category { get; set; }

    public string? Group { get; set; }

    [JsonConverter(typeof(LocalizedTextConverter))]
    public LocalizedText? Name { get; set; }

    [JsonConverter(typeof(LocalizedTextConverter))]
    public LocalizedText? Explanation { get; set; }

    public string? Risk { get; set; }

    public bool? DefaultEnabled { get; set; }

    public bool? RequiresAdmin { get; set; }

    public List<RootDefinition>? Roots { get; set; }

    public List<string>? Include { get; set; }

    public List<string>? Exclude { get; set; }

    public int? MinAgeDays { get; set; }

    public string? DeleteMode { get; set; }

    public List<string>? BlockingProcesses { get; set; }

    public bool? Recursive { get; set; }

    public bool? RemoveEmptyDirectories { get; set; }

    public string? Handler { get; set; }
}

internal sealed class RootDefinition
{
    public string? Base { get; set; }

    public string? Path { get; set; }
}

/// <summary>
/// Ad ve açıklama alanları hem düz metin hem de dil sözlüğü olarak yazılabilsin diye.
/// Kullanıcı kendi kuralını yazarken tek dil yeterli olmalı.
/// </summary>
internal sealed class LocalizedTextConverter : JsonConverter<LocalizedText>
{
    public override LocalizedText? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return LocalizedText.FromSingle(reader.GetString() ?? string.Empty);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            string language = reader.GetString() ?? string.Empty;

            if (!reader.Read())
            {
                break;
            }

            if (reader.TokenType == JsonTokenType.String && language.Length > 0)
            {
                values[language] = reader.GetString() ?? string.Empty;
            }
            else
            {
                reader.Skip();
            }
        }

        return new LocalizedText(values);
    }

    public override void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Resolve());
}
