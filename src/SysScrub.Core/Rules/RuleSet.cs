namespace SysScrub.Core.Rules;

/// <summary>Yüklenirken atlanan bir kural ve sebebi. Ayarlar ekranında gösterilir.</summary>
public sealed record RuleIssue(string Source, string? RuleId, string Message)
{
    public override string ToString() =>
        RuleId is null ? $"{Source}: {Message}" : $"{Source} [{RuleId}]: {Message}";
}

/// <summary>Yüklenmiş kural kümesi ve yükleme sırasında karşılaşılan sorunlar.</summary>
public sealed class RuleSet
{
    public RuleSet(IReadOnlyList<CleaningRule> rules, IReadOnlyList<RuleIssue> issues)
    {
        Rules = rules;
        Issues = issues;
    }

    public static RuleSet Empty { get; } = new([], []);

    public IReadOnlyList<CleaningRule> Rules { get; }

    /// <summary>Atlanan kurallar. Boş olmaması uygulamayı durdurmaz ama kullanıcıya bildirilir.</summary>
    public IReadOnlyList<RuleIssue> Issues { get; }

    public CleaningRule? Find(string id) =>
        Rules.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Arayüzün gruplama düzeni: kategori sırasına, sonra grup adına göre.</summary>
    public IReadOnlyList<RuleCategoryGroup> GroupForDisplay()
    {
        return Rules
            .GroupBy(r => r.Category)
            .OrderBy(g => (int)g.Key)
            .Select(categoryGroup => new RuleCategoryGroup(
                categoryGroup.Key,
                categoryGroup
                    .GroupBy(r => r.Group, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                    .Select(g => new RuleGroup(g.Key, g.OrderBy(r => (int)r.Risk).ToArray()))
                    .ToArray()))
            .ToArray();
    }
}

public sealed record RuleCategoryGroup(RuleCategory Category, IReadOnlyList<RuleGroup> Groups);

public sealed record RuleGroup(string Name, IReadOnlyList<CleaningRule> Rules);
