using System.Globalization;
using SysScrub.Core.Machine;
using Xunit;

namespace SysScrub.Core.Tests.Machine;

/// <summary>
/// Zamanlanmış bakım görevi.
///
/// Görevin kendisini kaydetmek yönetici hakkı ve gerçek Görev Zamanlayıcı istiyor;
/// burada yalnızca hesaplanan değerler sınanıyor. Kayıt akışı gerçek makinede
/// elle doğrulanıyor.
/// </summary>
public sealed class ScheduledMaintenanceTests
{
    /// <summary>Görev Zamanlayıcı ISO 8601 bekliyor; başka biçim sessizce reddediliyor.</summary>
    [Fact]
    public void BaslangicSaatiIsoBicimindeYazilir()
    {
        string boundary = ScheduledMaintenance.StartBoundary(3);

        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$", boundary);
        Assert.EndsWith("T03:00:00", boundary);
    }

    [Theory]
    [InlineData(0, "T00:00:00")]
    [InlineData(13, "T13:00:00")]
    [InlineData(23, "T23:00:00")]
    public void HerSaatDogruYazilir(int hour, string expected) =>
        Assert.EndsWith(expected, ScheduledMaintenance.StartBoundary(hour));

    /// <summary>Gün dışına taşan saat kırpılıyor; geçersiz tetikleyici üretmiyoruz.</summary>
    [Theory]
    [InlineData(-3, "T00:00:00")]
    [InlineData(48, "T23:00:00")]
    public void GunDisiSaatKirpilir(int hour, string expected) =>
        Assert.EndsWith(expected, ScheduledMaintenance.StartBoundary(hour));

    [Fact]
    public void BaslangicBugununTarihiniTasir() =>
        Assert.StartsWith(
            DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ScheduledMaintenance.StartBoundary(6));

    /// <summary>
    /// Görev adı sabit: değişirse eski kurulumlarda kalan görev bulunamaz ve
    /// kullanıcı iki görevle kalır.
    /// </summary>
    [Fact]
    public void GorevAdiSabit() => Assert.Equal("SysScrub Haftalık Bakım", ScheduledMaintenance.TaskName);

    /// <summary>Görev yoksa sorgu hata vermiyor; bu normal bir durum.</summary>
    [Fact]
    public void KayitliGorevYokkenSorguHataVermez()
    {
        MaintenanceTaskState state = new ScheduledMaintenance().Query();

        // Bu makinede görev kurulu olabilir de olmayabilir de; önemli olan çökmemesi.
        Assert.NotNull(state);
    }
}
