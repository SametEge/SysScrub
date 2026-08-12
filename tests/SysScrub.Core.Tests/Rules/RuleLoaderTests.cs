using SysScrub.Core.Rules;
using Xunit;

namespace SysScrub.Core.Tests.Rules;

public sealed class RuleLoaderTests
{
    private static readonly RuleSet Shipped = new RuleLoader().Load();

    // ------------------------------------------------------------------ gömülü paketler

    [Fact]
    public void GomuluKurallarSorunsuzYuklenir()
    {
        Assert.NotEmpty(Shipped.Rules);
        Assert.Empty(Shipped.Issues);
    }

    [Fact]
    public void HerKategoriTemsilEdiliyor()
    {
        foreach (RuleCategory category in (RuleCategory[])
                 [RuleCategory.Windows, RuleCategory.Browsers, RuleCategory.Applications,
                  RuleCategory.Gaming, RuleCategory.Developer, RuleCategory.Privacy])
        {
            Assert.Contains(Shipped.Rules, r => r.Category == category);
        }
    }

    [Fact]
    public void KimliklerBenzersiz()
    {
        var duplicates = Shipped.Rules
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void HerKuralinAdiVeAciklamasiVar()
    {
        foreach (CleaningRule rule in Shipped.Rules)
        {
            Assert.False(rule.Name.IsEmpty, $"{rule.Id} adsız");
            Assert.NotNull(rule.Explanation);
            Assert.False(rule.Explanation!.IsEmpty, $"{rule.Id} açıklamasız — 'Neden?' düğmesi boş kalır");
        }
    }

    [Fact]
    public void RiskliKurallarVarsayilanOlarakKapali()
    {
        // Kullanıcı bilinçli işaretlemeden gelişmiş bir kural asla çalışmamalı.
        foreach (CleaningRule rule in Shipped.Rules.Where(r => r.Risk == RiskLevel.Advanced))
        {
            Assert.False(rule.DefaultEnabled, $"{rule.Id} gelişmiş seviyede ama varsayılan açık");
        }
    }

    [Fact]
    public void KaliciSilmeYalnizcaGuvenliKurallardaKullanilir()
    {
        // Geri alınamayan silme, ancak kaybı olmayan içerikte veya kullanıcı
        // bilinçli açtığı gelişmiş kurallarda kabul edilebilir.
        foreach (CleaningRule rule in Shipped.Rules.Where(r => r.DeleteMode == DeleteMode.Permanent))
        {
            bool acceptable = rule.Risk == RiskLevel.Safe || !rule.DefaultEnabled;

            Assert.True(acceptable, $"{rule.Id} varsayılan açık, riskli ve kalıcı siliyor");
        }
    }

    [Fact]
    public void HerKuralinEnAzBirKokuVar() =>
        Assert.All(Shipped.Rules, rule => Assert.NotEmpty(rule.Roots));

    [Fact]
    public void KoklerdeKacisIfadesiYok() =>
        Assert.All(Shipped.Rules, rule =>
            Assert.All(rule.Roots, root => Assert.DoesNotContain("..", root.Path ?? string.Empty)));

    [Fact]
    public void GruplamaKategoriSirasiniKorur()
    {
        IReadOnlyList<RuleCategoryGroup> groups = Shipped.GroupForDisplay();

        Assert.NotEmpty(groups);
        Assert.Equal(RuleCategory.Windows, groups[0].Category);
        Assert.All(groups, g => Assert.NotEmpty(g.Groups));
    }

    // ------------------------------------------------------------------ hatalı girdiler

    [Fact]
    public void BozukJsonTumSetiDusurmez()
    {
        RuleSet set = RuleLoader.ParseDocument("{ bu json değil ");

        Assert.Empty(set.Rules);
        Assert.Single(set.Issues);
    }

    [Fact]
    public void KimliksizKuralAtlanirDigerleriYuklenir()
    {
        const string json = """
        {
          "rules": [
            { "name": "kimliksiz", "roots": [{ "base": "UserTemp" }] },
            { "id": "saglam", "name": "sağlam", "roots": [{ "base": "UserTemp" }] }
          ]
        }
        """;

        RuleSet set = RuleLoader.ParseDocument(json);

        Assert.Single(set.Rules);
        Assert.Equal("saglam", set.Rules[0].Id);
        Assert.Single(set.Issues);
    }

    [Fact]
    public void BilinmeyenKokReddedilir()
    {
        const string json = """
        { "rules": [ { "id": "x", "name": "x", "roots": [{ "base": "OlmayanKok" }] } ] }
        """;

        RuleSet set = RuleLoader.ParseDocument(json);

        Assert.Empty(set.Rules);
        Assert.Contains(set.Issues, i => i.Message.Contains("Bilinmeyen kök"));
    }

    [Fact]
    public void KacisIfadesiIceenKokReddedilir()
    {
        const string json = """
        { "rules": [ { "id": "x", "name": "x", "roots": [{ "base": "UserTemp", "path": "../../Windows" }] } ] }
        """;

        RuleSet set = RuleLoader.ParseDocument(json);

        Assert.Empty(set.Rules);
        Assert.Contains(set.Issues, i => i.Message.Contains(".."));
    }

    [Fact]
    public void DuzMetinAdKabulEdilir()
    {
        // Kullanıcı kendi kuralını yazarken çok dilli sözlük yazmak zorunda kalmamalı.
        const string json = """
        { "rules": [ { "id": "x", "name": "Basit ad", "roots": [{ "base": "UserTemp" }] } ] }
        """;

        RuleSet set = RuleLoader.ParseDocument(json);

        Assert.Equal("Basit ad", set.Rules[0].Name.Resolve());
    }

    [Fact]
    public void AyniKimlikSonrakiTanimlaEzilir()
    {
        const string json = """
        {
          "rules": [
            { "id": "x", "name": "ilk", "roots": [{ "base": "UserTemp" }] },
            { "id": "x", "name": "ikinci", "roots": [{ "base": "UserTemp" }] }
          ]
        }
        """;

        RuleSet set = RuleLoader.ParseDocument(json);

        Assert.Single(set.Rules);
        Assert.Equal("ikinci", set.Rules[0].Name.Resolve());
    }

    // ------------------------------------------------------------------ eşleştirme

    [Fact]
    public void DisAlmaIceAlmadanOnceGelir()
    {
        const string json = """
        {
          "rules": [{
            "id": "x", "name": "x",
            "roots": [{ "base": "UserTemp" }],
            "include": ["**/*"],
            "exclude": ["**/*.lock"]
          }]
        }
        """;

        CleaningRule rule = RuleLoader.ParseDocument(json).Rules[0];

        Assert.True(rule.Matches("a/b.tmp"));
        Assert.False(rule.Matches("a/b.lock"));
    }
}
