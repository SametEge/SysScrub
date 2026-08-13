using SysScrub.Core.Disks;
using Xunit;

namespace SysScrub.Core.Tests.Disks;

/// <summary>
/// Sağlık kararı.
///
/// İki kural sabit: bilmediğimize "iyi" demiyoruz, ve her karar gerekçesiyle
/// birlikte veriliyor. Buradaki bir gerileme kullanıcıyı ya boşuna korkutur
/// ya da arızalanmak üzere olan bir diske yeşil rozet takar.
/// </summary>
public sealed class DiskHealthEvaluatorTests
{
    private static NvmeHealth Nvme(
        byte criticalWarning = 0,
        int temperature = 45,
        byte spare = 100,
        byte spareThreshold = 10,
        byte used = 3,
        long mediaErrors = 0) => new()
    {
        CriticalWarning = criticalWarning,
        TemperatureCelsius = temperature,
        AvailableSpare = spare,
        AvailableSpareThreshold = spareThreshold,
        PercentageUsed = used,
        DataUnitsRead = 0,
        DataUnitsWritten = 0,
        PowerCycles = 0,
        PowerOnHours = 0,
        UnsafeShutdowns = 0,
        MediaErrors = mediaErrors,
        ErrorLogEntries = 0
    };

    private static SmartAttribute Attribute(
        byte id,
        byte current = 100,
        byte threshold = 0,
        long raw = 0,
        bool critical = false,
        string name = "Öznitelik") => new()
    {
        Id = id,
        Name = name,
        Current = current,
        Worst = current,
        Threshold = threshold,
        Raw = raw,
        IsCritical = critical
    };

    // ------------------------------------------------------------------ NVMe

    [Fact]
    public void SaglikliNvmeIyiSayilir()
    {
        (DiskHealthStatus status, string reason, int? percent) = DiskHealthEvaluator.Evaluate(Nvme());

        Assert.Equal(DiskHealthStatus.Good, status);
        Assert.Equal(97, percent);
        Assert.NotEmpty(reason);
    }

    /// <summary>Denetleyicinin kendi uyarısı her şeyin önünde: diski en iyi o biliyor.</summary>
    [Fact]
    public void DenetleyiciUyarisiHerSeyinOnundedir()
    {
        (DiskHealthStatus status, string reason, _) =
            DiskHealthEvaluator.Evaluate(Nvme(criticalWarning: 0x04));

        Assert.Equal(DiskHealthStatus.Bad, status);
        Assert.Contains("Güvenilirlik", reason);
    }

    [Fact]
    public void DuzeltilemeyenVeriHatasiKotuSayilir()
    {
        (DiskHealthStatus status, string reason, _) = DiskHealthEvaluator.Evaluate(Nvme(mediaErrors: 3));

        Assert.Equal(DiskHealthStatus.Bad, status);
        Assert.Contains("yedekleyin", reason);
    }

    [Fact]
    public void YedekBlokEsigeDusunceKotuSayilir()
    {
        (DiskHealthStatus status, _, _) =
            DiskHealthEvaluator.Evaluate(Nvme(spare: 10, spareThreshold: 10));

        Assert.Equal(DiskHealthStatus.Bad, status);
    }

    [Fact]
    public void YuzdeSeksenTuketilenOmurDikkatUretir()
    {
        (DiskHealthStatus status, _, int? percent) = DiskHealthEvaluator.Evaluate(Nvme(used: 85));

        Assert.Equal(DiskHealthStatus.Caution, status);
        Assert.Equal(15, percent);
    }

    /// <summary>Ömrü dolan disk çalışmaya devam edebilir; "kötü" demek abartı olur.</summary>
    [Fact]
    public void OmruDolanDiskKotuDegilDikkattir()
    {
        (DiskHealthStatus status, string reason, int? percent) = DiskHealthEvaluator.Evaluate(Nvme(used: 100));

        Assert.Equal(DiskHealthStatus.Caution, status);
        Assert.Equal(0, percent);
        Assert.Contains("yedeksiz bırakılmamalı", reason);
    }

    [Theory]
    [InlineData(69, DiskHealthStatus.Good)]
    [InlineData(70, DiskHealthStatus.Caution)]
    [InlineData(80, DiskHealthStatus.Bad)]
    public void SicaklikEsikleriUygulanir(int temperature, DiskHealthStatus expected)
    {
        (DiskHealthStatus status, _, _) = DiskHealthEvaluator.Evaluate(Nvme(temperature: temperature));

        Assert.Equal(expected, status);
    }

    // ------------------------------------------------------------------ ATA

    /// <summary>Veri okunamadıysa durum bilinmiyor kalır; yeşil rozet yanlış güven verir.</summary>
    [Fact]
    public void OzniteliksizDiskBilinmiyorKalir()
    {
        (DiskHealthStatus status, _, int? percent) = DiskHealthEvaluator.Evaluate([]);

        Assert.Equal(DiskHealthStatus.Unknown, status);
        Assert.Null(percent);
    }

    [Fact]
    public void SaglikliOzniteliklerIyiSayilir()
    {
        (DiskHealthStatus status, _, _) = DiskHealthEvaluator.Evaluate(
            [Attribute(0x09, raw: 2807), Attribute(0x0C, raw: 490)]);

        Assert.Equal(DiskHealthStatus.Good, status);
    }

    [Fact]
    public void EsiginAltinaDusenOznitelikKotuSayilir()
    {
        (DiskHealthStatus status, string reason, _) = DiskHealthEvaluator.Evaluate(
            [Attribute(0x05, current: 30, threshold: 36, name: "Yeniden atanan sektör sayısı")]);

        Assert.Equal(DiskHealthStatus.Bad, status);
        Assert.Contains("Yeniden atanan sektör sayısı", reason);
    }

    [Fact]
    public void OkunamayanSektorKotuSayilir()
    {
        (DiskHealthStatus status, string reason, _) = DiskHealthEvaluator.Evaluate(
            [Attribute(0xC6, raw: 4, critical: true)]);

        Assert.Equal(DiskHealthStatus.Bad, status);
        Assert.Contains("okunamıyor", reason);
    }

    [Fact]
    public void BekleyenSektorDikkatUretir()
    {
        (DiskHealthStatus status, string reason, _) = DiskHealthEvaluator.Evaluate(
            [Attribute(0xC5, raw: 8, critical: true)]);

        Assert.Equal(DiskHealthStatus.Caution, status);
        Assert.Contains("yedek alın", reason);
    }

    [Fact]
    public void YenidenAtananSektorDikkatUretir()
    {
        (DiskHealthStatus status, string reason, _) = DiskHealthEvaluator.Evaluate(
            [Attribute(0x05, raw: 12, critical: true)]);

        Assert.Equal(DiskHealthStatus.Caution, status);
        Assert.Contains("yedeğiyle değiştirilmiş", reason);
    }

    /// <summary>
    /// CRC hataları neredeyse her zaman kablo kaynaklı. Kullanıcıyı diski
    /// değiştirmeye yönlendirmek yanlış olur.
    /// </summary>
    [Fact]
    public void CrcHatasiKabloyaIsaretEder()
    {
        (DiskHealthStatus status, string reason, _) = DiskHealthEvaluator.Evaluate(
            [Attribute(0xC7, raw: 5, critical: true, name: "CRC hata sayısı")]);

        Assert.Equal(DiskHealthStatus.Caution, status);
        Assert.Contains("kablo", reason);
    }

    [Fact]
    public void KalanSsdOmruOzniteliktenOkunur()
    {
        (_, _, int? percent) = DiskHealthEvaluator.Evaluate([Attribute(0xE7, current: 88)]);

        Assert.Equal(88, percent);
    }

    /// <summary>Eşiğin altına düşen öznitelik, ham değer uyarılarının önüne geçer.</summary>
    [Fact]
    public void EsikIhlaliDigerUyarilardanOnceGelir()
    {
        (DiskHealthStatus status, string reason, _) = DiskHealthEvaluator.Evaluate(
        [
            Attribute(0xC5, raw: 8, critical: true),
            Attribute(0x05, current: 10, threshold: 36, name: "Yeniden atanan sektör sayısı")
        ]);

        Assert.Equal(DiskHealthStatus.Bad, status);
        Assert.Contains("eşiğin altına düştü", reason);
    }
}
