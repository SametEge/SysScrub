using System.Diagnostics;
using System.Globalization;
using System.Management;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SysScrub.Core.Drivers;

/// <summary>
/// Donanım envanterini okur.
///
/// İki WMI sınıfı birleştirilir:
///   Win32_PnPEntity        — cihaz adı, sınıfı, üreticisi, sorun kodu (hızlı)
///   Win32_PnPSignedDriver  — sürücü sürümü, tarihi, sağlayıcısı, INF, imza (yavaş)
///
/// SetupAPI daha zengin veri verir ama P/Invoke yükü buradaki ihtiyaca değmiyor;
/// eksik kalan tek şey donanım kimliklerinin tam sıralaması, onu da
/// Win32_PnPEntity.HardwareID sağlıyor.
/// </summary>
public sealed class DeviceInventory(ILogger<DeviceInventory>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<DeviceInventory>.Instance;

    public async Task<DeviceInventoryReport> LoadAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // WMI sorguları eşzamanlı ve yavaş; arayüz iş parçacığında çalıştırılamaz.
        DeviceInfo[] devices = await Task.Run(() => Load(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();

        _logger.LogInformation(
            "Donanım envanteri: {Count} cihaz, {Problems} sorunlu, {Elapsed} ms",
            devices.Length, devices.Count(d => d.HasProblem), stopwatch.ElapsedMilliseconds);

        return new DeviceInventoryReport { Devices = devices, Duration = stopwatch.Elapsed };
    }

    private DeviceInfo[] Load(CancellationToken cancellationToken)
    {
        Dictionary<string, DriverRecord> drivers = ReadSignedDrivers(cancellationToken);
        var devices = new List<DeviceInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Name, PNPClass, Manufacturer, ConfigManagerErrorCode, Present, HardwareID " +
                "FROM Win32_PnPEntity");

            using ManagementObjectCollection results = searcher.Get();

            foreach (ManagementBaseObject item in results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (item)
                {
                    string? deviceId = Text(item, "DeviceID");
                    string? name = Text(item, "Name");

                    // Adı olmayan cihazlar kullanıcıya bir şey ifade etmiyor.
                    if (deviceId is null || name is null)
                    {
                        continue;
                    }

                    int problemCode = Number(item, "ConfigManagerErrorCode");
                    bool present = Flag(item, "Present", defaultValue: true);

                    drivers.TryGetValue(deviceId, out DriverRecord driver);

                    devices.Add(new DeviceInfo
                    {
                        DeviceId = deviceId,
                        Name = name,
                        DeviceClass = Text(item, "PNPClass"),
                        Manufacturer = Text(item, "Manufacturer"),
                        HardwareIds = TextArray(item, "HardwareID"),
                        ProblemCode = problemCode,
                        Health = DetermineHealth(problemCode, present),
                        DriverVersion = driver.Version,
                        DriverDate = driver.Date,
                        DriverProvider = driver.Provider,
                        InfName = driver.InfName,
                        IsSigned = driver.IsSigned
                    });
                }
            }
        }
        catch (ManagementException ex)
        {
            _logger.LogError(ex, "Cihaz listesi okunamadı");
        }

        return devices.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    /// <summary>
    /// Sürücü kayıtları cihaz kimliğine göre indekslenir.
    /// Bu sorgu tek başına birkaç saniye sürebiliyor, bir kez çalıştırılıyor.
    /// </summary>
    private Dictionary<string, DriverRecord> ReadSignedDrivers(CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, DriverRecord>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, DriverVersion, DriverDate, DriverProviderName, InfName, IsSigned " +
                "FROM Win32_PnPSignedDriver");

            using ManagementObjectCollection results = searcher.Get();

            foreach (ManagementBaseObject item in results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (item)
                {
                    string? deviceId = Text(item, "DeviceID");

                    if (deviceId is null)
                    {
                        continue;
                    }

                    map[deviceId] = new DriverRecord(
                        Text(item, "DriverVersion"),
                        ParseWmiDate(Text(item, "DriverDate")),
                        Text(item, "DriverProviderName"),
                        Text(item, "InfName"),
                        Flag(item, "IsSigned", defaultValue: false));
                }
            }
        }
        catch (ManagementException ex)
        {
            // Sürücü ayrıntısı alınamazsa cihaz listesi yine gösterilir, sürümler boş kalır.
            _logger.LogWarning(ex, "Sürücü ayrıntıları okunamadı");
        }

        return map;
    }

    private static DeviceHealth DetermineHealth(int problemCode, bool present) => problemCode switch
    {
        22 => DeviceHealth.Disabled,
        _ when !present => DeviceHealth.NotPresent,
        0 => DeviceHealth.Working,
        _ => DeviceHealth.Problem
    };

    /// <summary>WMI tarihleri "20240115000000.000000+000" biçiminde geliyor.</summary>
    private static DateTime? ParseWmiDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 8)
        {
            return null;
        }

        return DateTime.TryParseExact(
            value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
            ? parsed
            : null;
    }

    private static string? Text(ManagementBaseObject item, string property)
    {
        try
        {
            string? value = item[property]?.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (ManagementException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> TextArray(ManagementBaseObject item, string property)
    {
        try
        {
            return item[property] is string[] values ? values : [];
        }
        catch (ManagementException)
        {
            return [];
        }
    }

    private static int Number(ManagementBaseObject item, string property)
    {
        try
        {
            return item[property] is null ? 0 : Convert.ToInt32(item[property], CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is ManagementException or FormatException or InvalidCastException)
        {
            return 0;
        }
    }

    private static bool Flag(ManagementBaseObject item, string property, bool defaultValue)
    {
        try
        {
            return item[property] is bool value ? value : defaultValue;
        }
        catch (ManagementException)
        {
            return defaultValue;
        }
    }

    private readonly record struct DriverRecord(
        string? Version,
        DateTime? Date,
        string? Provider,
        string? InfName,
        bool IsSigned);
}
