using System.Buffers.Binary;
using SysScrub.Core.Disks;
using Xunit;

namespace SysScrub.Core.Tests.Disks;

/// <summary>
/// NVMe sağlık günlüğü ayrıştırıcısı.
///
/// Alan konumları NVMe standardında sabit. Bir baytlık kayma sıcaklığı yanlış
/// okutur ve kullanıcıya var olmayan bir arıza gösterir; bu yüzden her alan
/// kendi testiyle sabitleniyor. Donanım gerekmiyor: tampon burada kuruluyor.
/// </summary>
public sealed class NvmeHealthParserTests
{
    /// <summary>Gerçek bir günlüğün taşıdığı değerlerle 512 baytlık tampon kurar.</summary>
    private static byte[] BuildLog(
        byte criticalWarning = 0,
        int temperatureCelsius = 47,
        byte availableSpare = 100,
        byte spareThreshold = 10,
        byte percentageUsed = 3,
        long dataUnitsRead = 0,
        long dataUnitsWritten = 0,
        long powerCycles = 0,
        long powerOnHours = 0,
        long unsafeShutdowns = 0,
        long mediaErrors = 0,
        int sensor1Celsius = 0)
    {
        var log = new byte[512];

        log[0] = criticalWarning;
        BinaryPrimitives.WriteUInt16LittleEndian(log.AsSpan(1, 2), (ushort)(temperatureCelsius + 273));
        log[3] = availableSpare;
        log[4] = spareThreshold;
        log[5] = percentageUsed;

        WriteCounter(log, 32, dataUnitsRead);
        WriteCounter(log, 48, dataUnitsWritten);
        WriteCounter(log, 112, powerCycles);
        WriteCounter(log, 128, powerOnHours);
        WriteCounter(log, 144, unsafeShutdowns);
        WriteCounter(log, 160, mediaErrors);

        if (sensor1Celsius > 0)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(log.AsSpan(200, 2), (ushort)(sensor1Celsius + 273));
        }

        return log;
    }

    private static void WriteCounter(byte[] log, int offset, long value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(offset, 8), (ulong)value);

    // ------------------------------------------------------------------ sıcaklık

    [Fact]
    public void SicaklikKelvindenSantigradaCevrilir() =>
        Assert.Equal(47, NvmeHealthReader.Parse(BuildLog(temperatureCelsius: 47)).TemperatureCelsius);

    /// <summary>
    /// Sıfır Kelvin "bildirilmedi" demek. Çıkarma yapılırsa ekranda -273 °C çıkar.
    /// </summary>
    [Fact]
    public void BildirilmeyenSicaklikSifirKalir()
    {
        var log = new byte[512];

        Assert.Equal(0, NvmeHealthReader.Parse(log).TemperatureCelsius);
    }

    [Fact]
    public void EkSensorlerOkunur()
    {
        NvmeHealth health = NvmeHealthReader.Parse(BuildLog(sensor1Celsius: 75));

        Assert.Equal(75, Assert.Single(health.SensorsCelsius));
    }

    [Fact]
    public void BildirmeyenSensorListeyeGirmez() =>
        Assert.Empty(NvmeHealthReader.Parse(BuildLog()).SensorsCelsius);

    // ------------------------------------------------------------------ sayaçlar

    [Fact]
    public void SayaclarDogruKonumdanOkunur()
    {
        NvmeHealth health = NvmeHealthReader.Parse(BuildLog(
            dataUnitsRead: 43_456_789,
            dataUnitsWritten: 27_612_345,
            powerCycles: 490,
            powerOnHours: 2807,
            unsafeShutdowns: 115,
            mediaErrors: 0));

        Assert.Equal(43_456_789, health.DataUnitsRead);
        Assert.Equal(27_612_345, health.DataUnitsWritten);
        Assert.Equal(490, health.PowerCycles);
        Assert.Equal(2807, health.PowerOnHours);
        Assert.Equal(115, health.UnsafeShutdowns);
        Assert.Equal(0, health.MediaErrors);
    }

    /// <summary>
    /// NVMe yazma miktarını 1000 × 512 baytlık birimlerle sayıyor. Doğrudan
    /// 512 ile çarpmak yaygın bir hata ve sonucu bin kat küçük gösterir.
    /// </summary>
    [Fact]
    public void YazilanVeriBinKereBesYuzOnIkiBaytBirimiyleHesaplanir()
    {
        NvmeHealth health = NvmeHealthReader.Parse(BuildLog(dataUnitsWritten: 1000));

        Assert.Equal(512_000_000, health.BytesWritten);
    }

    [Fact]
    public void OkunanVeriAyniBirimiKullanir() =>
        Assert.Equal(512_000, NvmeHealthReader.Parse(BuildLog(dataUnitsRead: 1)).BytesRead);

    // ------------------------------------------------------------------ kritik uyarılar

    [Fact]
    public void UyariYokkenListeBos() =>
        Assert.Empty(NvmeHealthReader.Parse(BuildLog()).CriticalWarnings);

    [Theory]
    [InlineData(0x01, "Yedek blok")]
    [InlineData(0x02, "Sıcaklık")]
    [InlineData(0x04, "Güvenilirlik")]
    [InlineData(0x08, "salt okunur")]
    public void KritikUyariBitleriCozulur(byte flag, string expectedFragment)
    {
        NvmeHealth health = NvmeHealthReader.Parse(BuildLog(criticalWarning: flag));

        Assert.Contains(expectedFragment, Assert.Single(health.CriticalWarnings));
    }

    [Fact]
    public void BirdenCokUyariAyriAyriListelenir()
    {
        NvmeHealth health = NvmeHealthReader.Parse(BuildLog(criticalWarning: 0x01 | 0x04));

        Assert.Equal(2, health.CriticalWarnings.Count);
    }

    // ------------------------------------------------------------------ yedek blok

    [Fact]
    public void YedekBlokVeEsigiAyriOkunur()
    {
        NvmeHealth health = NvmeHealthReader.Parse(BuildLog(availableSpare: 100, spareThreshold: 10));

        Assert.Equal(100, health.AvailableSpare);
        Assert.Equal(10, health.AvailableSpareThreshold);
    }
}
