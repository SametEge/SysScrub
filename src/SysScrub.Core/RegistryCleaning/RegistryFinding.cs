using SysScrub.Core.Rules;

namespace SysScrub.Core.RegistryCleaning;

/// <summary>
/// Ölü olduğu tespit edilen tek bir registry kaydı.
///
/// Her bulgu, neden ölü sayıldığını taşımak zorunda: kullanıcı silmeden önce
/// gerekçeyi görebilmeli. "12 sorun bulundu" deyip geçen temizleyicilerin
/// güvenilmez olmasının sebebi tam olarak bu bilginin gizlenmesi.
/// </summary>
public sealed record RegistryFinding
{
    public required string ScannerId { get; init; }

    public required RegistryLocation Location { get; init; }

    /// <summary>Neden ölü sayıldığı: "İşaret ettiği dosya yok".</summary>
    public required string Reason { get; init; }

    /// <summary>İşaret ettiği ve bulunamayan hedef: dosya yolu, CLSID, ProgID.</summary>
    public string? Target { get; init; }

    public RiskLevel Risk { get; init; } = RiskLevel.Safe;

    public override string ToString() => $"{Location.DisplayPath} — {Reason}";
}

/// <summary>Bir tarayıcının sonucu.</summary>
public sealed record RegistryScannerResult
{
    public required IRegistryScanner Scanner { get; init; }

    public required IReadOnlyList<RegistryFinding> Findings { get; init; }

    public int Count => Findings.Count;

    public bool HasFindings => Findings.Count > 0;
}

/// <summary>Tam registry tarama raporu.</summary>
public sealed record RegistryScanReport
{
    public required IReadOnlyList<RegistryScannerResult> Results { get; init; }

    public required TimeSpan Duration { get; init; }

    public static RegistryScanReport Empty { get; } = new() { Results = [], Duration = TimeSpan.Zero };

    public IReadOnlyList<RegistryScannerResult> WithFindings =>
        Results.Where(r => r.HasFindings).ToArray();

    public int TotalCount => Results.Sum(r => r.Count);
}

public sealed record RegistryScanProgress
{
    public required string CurrentScanner { get; init; }

    public required int Completed { get; init; }

    public required int Total { get; init; }

    public required int FindingsSoFar { get; init; }

    public double Fraction => Total == 0 ? 0d : (double)Completed / Total;
}

/// <summary>Tek bir tarayıcı. Her biri tek bir ölü kayıt türünü arar.</summary>
public interface IRegistryScanner
{
    string Id { get; }

    string Title { get; }

    /// <summary>Kullanıcının "neden?" sorusuna cevap: bu tarayıcının ne aradığı.</summary>
    string Explanation { get; }

    RiskLevel Risk { get; }

    bool DefaultEnabled { get; }

    bool RequiresAdmin { get; }

    IEnumerable<RegistryFinding> Scan(CancellationToken cancellationToken);
}
