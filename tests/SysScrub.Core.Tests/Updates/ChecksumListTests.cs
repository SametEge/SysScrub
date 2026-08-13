using System.Security.Cryptography;
using System.Text;
using SysScrub.Core.Updates;
using Xunit;

namespace SysScrub.Core.Tests.Updates;

public class ChecksumListTests
{
    /// <summary>release.yml'nin ürettiği biçim: özet, iki boşluk, dosya adı.</summary>
    private const string Sample = """
    E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855  SysScrub-Setup-0.14.0-alpha.exe
    9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08  SysScrub-0.14.0-portable-x64.zip
    """;

    [Fact]
    public void Satirlar_okunur()
    {
        ChecksumList list = ChecksumList.Parse(Sample);

        Assert.Equal(2, list.Count);
        Assert.Equal(
            "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
            list.Find("SysScrub-Setup-0.14.0-alpha.exe"));
    }

    [Fact]
    public void Listede_olmayan_dosya_null_doner()
    {
        Assert.Null(ChecksumList.Parse(Sample).Find("baska.exe"));
    }

    [Fact]
    public void Bozuk_satirlar_atlanir()
    {
        const string messy = """
        # yorum satırı

        kisa  dosya.exe
        E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855
        E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855  iyi.exe
        """;

        ChecksumList list = ChecksumList.Parse(messy);

        Assert.Equal(1, list.Count);
        Assert.NotNull(list.Find("iyi.exe"));
    }

    /// <summary>sha256sum ikili kipte dosya adının başına yıldız koyuyor.</summary>
    [Fact]
    public void Ikili_kip_yildizi_dosya_adina_karismaz()
    {
        ChecksumList list = ChecksumList.Parse(
            "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855 *kurulum.exe");

        Assert.NotNull(list.Find("kurulum.exe"));
    }

    [Fact]
    public void Windows_satir_sonlari_temizlenir()
    {
        ChecksumList list = ChecksumList.Parse(
            "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855  kurulum.exe\r\n");

        Assert.NotNull(list.Find("kurulum.exe"));
    }

    [Fact]
    public async Task Dosya_ozeti_dogru_hesaplaniyor()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sysscrub-hash-{Guid.NewGuid():N}.bin");

        try
        {
            byte[] content = Encoding.UTF8.GetBytes("SysScrub");
            await File.WriteAllBytesAsync(path, content);

            string expected = Convert.ToHexString(SHA256.HashData(content));

            Assert.Equal(expected, await ChecksumList.ComputeAsync(path, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
