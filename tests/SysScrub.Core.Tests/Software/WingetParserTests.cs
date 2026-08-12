using SysScrub.Core.Software;
using Xunit;

namespace SysScrub.Core.Tests.Software;

/// <summary>
/// Ayrıştırıcı gerçek winget çıktısıyla sınanıyor. Sütun başlıkları Windows'un
/// diline göre değiştiği için ayrıştırıcı başlık adına bakmıyor; testler bunu doğruluyor.
/// </summary>
public sealed class WingetParserTests
{
    // Gerçek "winget upgrade --include-unknown" çıktısından alınmış örnek.
    private const string RealOutput =
        """
        Name                                             Id                                     Version              Available           Source
        ----------------------------------------------------------------------------------------------------------------------------------------
        Affinity                                         Canva.Affinity                         3.2.0.4351           3.2.3.4646          winget
        Antigravity 2.1.4                                Google.Antigravity                     2.1.4                2.6.0               winget
        Cursor (User)                                    Anysphere.Cursor                       3.11.19              3.14.27             winget
        Epic Games Launcher                              XP99VR1BPSBQJ2                         1.3.175.0            1.3.189.0           msstore
        Microsoft Teams                                  Microsoft.Teams                        25332.1210.4188.1171 26198.304.4946.9672 winget

        5 upgrades available.
        """;

    [Fact]
    public void GercekCiktiAyristirilir()
    {
        IReadOnlyList<SoftwareUpdate> updates = WingetService.ParseUpgradeTable(RealOutput);

        Assert.Equal(5, updates.Count);
    }

    [Fact]
    public void AlanlarDogruSutunlardanOkunur()
    {
        SoftwareUpdate update = WingetService.ParseUpgradeTable(RealOutput)[0];

        Assert.Equal("Affinity", update.Name);
        Assert.Equal("Canva.Affinity", update.Id);
        Assert.Equal("3.2.0.4351", update.InstalledVersion);
        Assert.Equal("3.2.3.4646", update.AvailableVersion);
        Assert.Equal("winget", update.Source);
    }

    [Fact]
    public void BosluklarIceenAdlarBozulmaz()
    {
        SoftwareUpdate update = WingetService.ParseUpgradeTable(RealOutput)[1];

        Assert.Equal("Antigravity 2.1.4", update.Name);
        Assert.Equal("Google.Antigravity", update.Id);
    }

    [Fact]
    public void SutunuTamDolduranSurumKesilmez()
    {
        // "25332.1210.4188.1171" sütun genişliğini tam dolduruyor; bir karakter
        // kayması bu değeri bitişik sütuna taşırdı.
        SoftwareUpdate teams = WingetService.ParseUpgradeTable(RealOutput)
            .Single(u => u.Id == "Microsoft.Teams");

        Assert.Equal("25332.1210.4188.1171", teams.InstalledVersion);
        Assert.Equal("26198.304.4946.9672", teams.AvailableVersion);
    }

    [Fact]
    public void MagazaKaynagiAyirtEdilir()
    {
        SoftwareUpdate epic = WingetService.ParseUpgradeTable(RealOutput)
            .Single(u => u.Id == "XP99VR1BPSBQJ2");

        Assert.True(epic.IsFromStore);
    }

    [Fact]
    public void OzetSatiriPaketSayilmaz()
    {
        // "5 upgrades available." satırı listeye girmemeli.
        Assert.DoesNotContain(WingetService.ParseUpgradeTable(RealOutput), u => u.Id.Contains("upgrades"));
    }

    [Fact]
    public void TurkceBasliklarlaDaCalisir()
    {
        // Başlık adları değişse bile sütun konumları aynı mantıkla bulunur.
        const string turkish =
            """
            Ad                     Kimlik                 Sürüm       Kullanılabilir Kaynak
            ---------------------------------------------------------------------------------
            Bir Program            Ornek.Program          1.0.0       2.0.0          winget
            """;

        SoftwareUpdate update = Assert.Single(WingetService.ParseUpgradeTable(turkish));

        Assert.Equal("Bir Program", update.Name);
        Assert.Equal("Ornek.Program", update.Id);
        Assert.Equal("1.0.0", update.InstalledVersion);
        Assert.Equal("2.0.0", update.AvailableVersion);
    }

    [Fact]
    public void BilinmeyenSurumIsaretlenir()
    {
        const string output =
            """
            Name          Id             Version   Available  Source
            ----------------------------------------------------------
            Bir Uygulama  Ornek.Uygulama Unknown   3.0.0      winget
            """;

        SoftwareUpdate update = Assert.Single(WingetService.ParseUpgradeTable(output));

        Assert.True(update.IsInstalledVersionUnknown);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Güncellenecek bir şey yok.")]
    [InlineData("Name  Id  Version")]
    public void GecersizCiktiBosListeDoner(string output) =>
        Assert.Empty(WingetService.ParseUpgradeTable(output));

    [Fact]
    public void IlerlemeSatirlariAtlanir()
    {
        // winget tablodan önce eğik çizgi animasyonu yazabiliyor.
        const string output =
            """
            \
            |
            /
            Name          Id             Version   Available  Source
            ----------------------------------------------------------
            Bir Uygulama  Ornek.Uygulama 1.0.0     2.0.0      winget
            """;

        Assert.Single(WingetService.ParseUpgradeTable(output));
    }
}
