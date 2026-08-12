using SysScrub.Core.System;
using Xunit;

namespace SysScrub.Core.Tests.System;

public sealed class SystemInfoServiceTests
{
    private readonly SystemSnapshot _snapshot = new SystemInfoService().Capture();

    [Fact]
    public void IsletimSistemiVeMakineAdiDolu()
    {
        Assert.False(string.IsNullOrWhiteSpace(_snapshot.OperatingSystem));
        Assert.False(string.IsNullOrWhiteSpace(_snapshot.MachineName));
    }

    [Fact]
    public void EnAzBirSurucuBulunur() => Assert.NotEmpty(_snapshot.Drives);

    [Fact]
    public void SistemSurucusuTespitEdilir() => Assert.NotNull(_snapshot.SystemDrive);

    [Fact]
    public void SurucuBoyutlariTutarli()
    {
        foreach (DriveSnapshot drive in _snapshot.Drives)
        {
            Assert.True(drive.TotalBytes > 0, $"{drive.Name} toplam boyut sıfır");
            Assert.InRange(drive.FreeBytes, 0, drive.TotalBytes);
            Assert.InRange(drive.UsedRatio, 0d, 1d);
        }
    }

    [Fact]
    public void BellekOkunabiliyor()
    {
        Assert.True(_snapshot.Memory.TotalBytes > 0);
        Assert.True(_snapshot.Memory.AvailableBytes <= _snapshot.Memory.TotalBytes);
        Assert.InRange(_snapshot.Memory.UsedRatio, 0d, 1d);
    }

    [Fact]
    public void CalismaSuresiMakul() => Assert.True(_snapshot.Uptime > TimeSpan.Zero);
}
