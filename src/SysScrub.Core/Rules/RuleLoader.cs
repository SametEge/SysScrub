using System.Reflection;
using System.Text.Json;
using SysScrub.Core.Machine;

namespace SysScrub.Core.Rules;

/// <summary>
/// Kuralları yükler: önce uygulamaya gömülü paketler, sonra kullanıcının
/// %ProgramData%\SysScrub\rules klasöründeki dosyalar.
///
/// Aynı kimlikli kural iki yerde varsa kullanıcının dosyası kazanır — böylece
/// bir kuralı düzeltmek için uygulamayı güncellemek gerekmez.
///
/// Bozuk bir kural yalnızca kendini düşürür; kalan kurallar yüklenmeye devam eder.
/// </summary>
public sealed class RuleLoader
{
    private const string EmbeddedPrefix = "SysScrub.Core.Rules.Definitions.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public RuleSet Load()
    {
        var rules = new Dictionary<string, CleaningRule>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<RuleIssue>();

        foreach ((string source, string json) in ReadEmbedded())
        {
            Merge(rules, issues, source, json);
        }

        foreach ((string source, string json) in ReadUserRules(issues))
        {
            Merge(rules, issues, source, json);
        }

        return new RuleSet(rules.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray(), issues);
    }

    /// <summary>Testlerin ve doğrulama aracının tek bir belgeyi ayrıştırması için.</summary>
    public static RuleSet ParseDocument(string json, string source = "(bellek)")
    {
        var rules = new Dictionary<string, CleaningRule>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<RuleIssue>();

        Merge(rules, issues, source, json);

        return new RuleSet(rules.Values.ToArray(), issues);
    }

    private static IEnumerable<(string Source, string Json)> ReadEmbedded()
    {
        Assembly assembly = typeof(RuleLoader).Assembly;

        foreach (string name in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(EmbeddedPrefix, StringComparison.Ordinal))
                     .Where(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(n => n, StringComparer.Ordinal))
        {
            using Stream? stream = assembly.GetManifestResourceStream(name);

            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            yield return (name[EmbeddedPrefix.Length..], reader.ReadToEnd());
        }
    }

    private static IEnumerable<(string Source, string Json)> ReadUserRules(List<RuleIssue> issues)
    {
        string directory = AppPaths.RulesDirectory;

        if (!Directory.Exists(directory))
        {
            yield break;
        }

        string[] files;

        try
        {
            files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add(new RuleIssue(directory, null, $"Kullanıcı kuralları okunamadı: {ex.Message}"));
            yield break;
        }

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        foreach (string file in files)
        {
            string json;

            try
            {
                json = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(new RuleIssue(Path.GetFileName(file), null, $"Dosya okunamadı: {ex.Message}"));
                continue;
            }

            yield return (Path.GetFileName(file), json);
        }
    }

    private static void Merge(
        Dictionary<string, CleaningRule> rules,
        List<RuleIssue> issues,
        string source,
        string json)
    {
        RuleDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<RuleDocument>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            issues.Add(new RuleIssue(source, null, $"JSON ayrıştırılamadı: {ex.Message}"));
            return;
        }

        if (document is null)
        {
            issues.Add(new RuleIssue(source, null, "Dosya boş."));
            return;
        }

        foreach (RuleDefinition definition in document.Rules)
        {
            if (TryConvert(definition, source, issues, out CleaningRule? rule))
            {
                rules[rule.Id] = rule;
            }
        }
    }

    private static bool TryConvert(
        RuleDefinition definition,
        string source,
        List<RuleIssue> issues,
        out CleaningRule rule)
    {
        rule = null!;

        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            issues.Add(new RuleIssue(source, null, "Kimliği olmayan kural atlandı."));
            return false;
        }

        string id = definition.Id.Trim();

        if (definition.Name is null || definition.Name.IsEmpty)
        {
            issues.Add(new RuleIssue(source, id, "Adı olmayan kural atlandı."));
            return false;
        }

        if (definition.Roots is null || definition.Roots.Count == 0)
        {
            issues.Add(new RuleIssue(source, id, "Hedef klasörü olmayan kural atlandı."));
            return false;
        }

        var roots = new List<RuleRoot>(definition.Roots.Count);

        foreach (RootDefinition root in definition.Roots)
        {
            if (!Enum.TryParse(root.Base, ignoreCase: true, out PathToken token))
            {
                issues.Add(new RuleIssue(source, id, $"Bilinmeyen kök: '{root.Base}'."));
                return false;
            }

            // Derinlemesine savunma: kaçış ifadesi SafetyGuard'da da yakalanır ama
            // kuralın hiç oraya kadar gelmemesi daha iyi.
            if (root.Path is not null && root.Path.Contains("..", StringComparison.Ordinal))
            {
                issues.Add(new RuleIssue(source, id, "Kök yolunda '..' kullanılamaz."));
                return false;
            }

            roots.Add(new RuleRoot { Base = token, Path = string.IsNullOrWhiteSpace(root.Path) ? null : root.Path.Trim() });
        }

        if (definition.MinAgeDays is < 0)
        {
            issues.Add(new RuleIssue(source, id, "minAgeDays negatif olamaz."));
            return false;
        }

        rule = new CleaningRule
        {
            Id = id,
            Category = ParseEnum(definition.Category, RuleCategory.Other),
            Group = string.IsNullOrWhiteSpace(definition.Group) ? definition.Name.Resolve() : definition.Group.Trim(),
            Name = definition.Name,
            Explanation = definition.Explanation,
            Risk = ParseEnum(definition.Risk, RiskLevel.Safe),
            DefaultEnabled = definition.DefaultEnabled ?? true,
            RequiresAdmin = definition.RequiresAdmin ?? false,
            Roots = roots,
            Include = ParsePatterns(definition.Include) ?? [GlobPattern.MatchAll],
            Exclude = ParsePatterns(definition.Exclude) ?? [],
            MinAgeDays = definition.MinAgeDays ?? 0,
            DeleteMode = ParseEnum(definition.DeleteMode, DeleteMode.Quarantine),
            BlockingProcesses = definition.BlockingProcesses?.Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim()).ToArray() ?? [],
            Recursive = definition.Recursive ?? true,
            RemoveEmptyDirectories = definition.RemoveEmptyDirectories ?? true,
            Handler = string.IsNullOrWhiteSpace(definition.Handler) ? null : definition.Handler.Trim()
        };

        return true;
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out TEnum parsed) ? parsed : fallback;

    private static IReadOnlyList<GlobPattern>? ParsePatterns(List<string>? patterns)
    {
        if (patterns is null || patterns.Count == 0)
        {
            return null;
        }

        return patterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(GlobPattern.Parse)
            .ToArray();
    }
}
