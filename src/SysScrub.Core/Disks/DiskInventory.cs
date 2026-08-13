using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace SysScrub.Core.Disks;

/// <summary>
/// Fiziksel diskleri bulur ve her biri için uygun okuma yolunu dener.
///
/// Erişim iki kademeli. Kimlik bilgisi (model, seri no, veri yolu) sıfır erişim
/// hakkıyla açılan tanıtıcıdan okunuyor ve yönetici gerektirmiyor. Sağlık verisi
/// sürücüye komut göndermek demek; bu okuma/yazma hakkı, yani yönetici istiyor.
///
/// Okunamayan disk sessizce kaybolmuyor: listede kalıyor ve neden okunamadığı
/// yazıyor. "Görünmüyorsa sorun yoktur" diye bir şey yok.
/// </summary>
public sealed class DiskInventory(
    SmartAttributeTable? table = null,
    ILogger<DiskInventory>? logger = null)
{
    /// <summary>Windows'un desteklediği en fazla fiziksel disk sayısı kadar deniyoruz.</summary>
    private const int MaxDrives = 32;

    private readonly SmartAttributeTable _table = table ?? new SmartAttributeTable();
    private readonly ILogger _logger = logger ?? NullLogger<DiskInventory>.Instance;

    public Task<DiskHealthReport> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(cancellationToken), cancellationToken);

    private DiskHealthReport Load(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var disks = new List<DiskInfo>();

        bool elevated = IsElevated();

        for (int index = 0; index < MaxDrives; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DiskInfo? disk = TryReadDisk(index, elevated);

            if (disk is not null)
            {
                disks.Add(disk);
            }
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Disk envanteri: {Count} disk, {Readable} tanesinde SMART okundu, {Elapsed} ms",
            disks.Count, disks.Count(d => d.HasSmartData), stopwatch.ElapsedMilliseconds);

        return new DiskHealthReport
        {
            Disks = disks,
            Duration = stopwatch.Elapsed,
            IsElevated = elevated
        };
    }

    private DiskInfo? TryReadDisk(int index, bool elevated)
    {
        string path = $@"\\.\PhysicalDrive{index}";

        // Kimlik için erişim hakkı istemiyoruz: sıfır hakla açılan tanıtıcı
        // sorgu göndermeye yetiyor ve yönetici gerektirmiyor.
        using SafeFileHandle identity = Open(path, 0);

        if (identity.IsInvalid)
        {
            return null;
        }

        (string model, string? serial, string? revision, uint busType, bool removable) = ReadDescriptor(identity);

        DiskInfo baseInfo = new()
        {
            Index = index,
            Model = model.Length > 0 ? model : $"Disk {index}",
            SerialNumber = serial,
            FirmwareRevision = revision,
            CapacityBytes = ReadCapacity(identity),
            BusType = DiskBusType.Describe(busType),
            IsSolidState = ReadIsSolidState(identity),
            IsRemovable = removable,
            AccessMethod = SmartAccessMethod.None,
            Status = DiskHealthStatus.Unknown,
            StatusReason = string.Empty
        };

        if (!elevated)
        {
            return baseInfo with
            {
                StatusReason = "S.M.A.R.T. verisi yönetici hakkı olmadan okunamıyor.",
                AccessMessage = "Yönetici olarak çalıştırıldığında disk sağlığı görünür."
            };
        }

        // Sağlık verisi için okuma/yazma hakkı gerekiyor.
        using SafeFileHandle health = Open(path, DiskNative.GenericRead | DiskNative.GenericWrite);

        if (health.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();

            return baseInfo with
            {
                StatusReason = "Diske sağlık sorgusu gönderilemedi.",
                AccessMessage = $"Disk açılamadı (hata {error})."
            };
        }

        return busType == DiskBusType.Nvme
            ? ReadNvme(baseInfo, health)
            : ReadAta(baseInfo, health, index);
    }

    private static DiskInfo ReadNvme(DiskInfo disk, SafeFileHandle handle)
    {
        NvmeHealth? nvme = NvmeHealthReader.TryRead(handle, out string? message);

        if (nvme is null)
        {
            return disk with
            {
                StatusReason = "NVMe sağlık günlüğü okunamadı.",
                AccessMessage = message
            };
        }

        (DiskHealthStatus status, string reason, int? percent) = DiskHealthEvaluator.Evaluate(nvme);

        // Bellenim sürümü ve seri numarası tanımlayıcıda eksik kalabiliyor;
        // NVMe Identify yapısı ikisini de kesin veriyor.
        (string? serial, string? firmware, string? model) = NvmeHealthReader.TryReadIdentity(handle);

        return disk with
        {
            AccessMethod = SmartAccessMethod.Nvme,
            Nvme = nvme,
            Status = status,
            StatusReason = reason,
            HealthPercent = percent,
            SerialNumber = disk.SerialNumber ?? serial,
            FirmwareRevision = disk.FirmwareRevision ?? firmware,
            Model = string.IsNullOrWhiteSpace(disk.Model) || disk.Model.StartsWith("Disk ", StringComparison.Ordinal)
                ? model ?? disk.Model
                : disk.Model,
            IsSolidState = true
        };
    }

    private DiskInfo ReadAta(DiskInfo disk, SafeFileHandle handle, int index)
    {
        IReadOnlyList<RawAtaAttribute> raw = AtaHealthReader.TryRead(handle, (byte)index, out string? message);

        if (raw.Count == 0)
        {
            return disk with
            {
                StatusReason = "S.M.A.R.T. verisi okunamadı.",
                // USB kutularının çoğu ATA komutlarını geçirmiyor; sebebi bu.
                AccessMessage = message ?? "Bu bağlantı üzerinden S.M.A.R.T. okunamıyor."
            };
        }

        SmartAttribute[] attributes = raw.Select(_table.Describe).ToArray();

        (DiskHealthStatus status, string reason, int? percent) = DiskHealthEvaluator.Evaluate(attributes);

        return disk with
        {
            AccessMethod = SmartAccessMethod.Ata,
            Attributes = attributes,
            Status = status,
            StatusReason = reason,
            HealthPercent = percent
        };
    }

    // ------------------------------------------------------------------ tanıtıcı okuma

    private static SafeFileHandle Open(string path, uint access) =>
        DiskNative.CreateFile(
            path,
            access,
            DiskNative.FileShareRead | DiskNative.FileShareWrite,
            IntPtr.Zero,
            DiskNative.OpenExisting,
            0,
            IntPtr.Zero);

    /// <summary>
    /// STORAGE_DEVICE_DESCRIPTOR. Metin alanları yapının içinde değil, sonuna
    /// eklenmiş; yapı yalnızca konumlarını veriyor.
    /// </summary>
    private static (string Model, string? Serial, string? Revision, uint BusType, bool Removable)
        ReadDescriptor(SafeFileHandle handle)
    {
        const int bufferSize = 1024;

        IntPtr query = Marshal.AllocHGlobal(DiskNative.PropertyQueryHeaderSize + 4);
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            Marshal.WriteInt32(query, 0, (int)DiskNative.StorageDeviceProperty);
            Marshal.WriteInt32(query, 4, (int)DiskNative.PropertyStandardQuery);
            Marshal.WriteInt32(query, 8, 0);

            bool ok = DiskNative.DeviceIoControl(
                handle,
                DiskNative.IoctlStorageQueryProperty,
                query,
                DiskNative.PropertyQueryHeaderSize + 4,
                buffer,
                bufferSize,
                out int returned,
                IntPtr.Zero);

            if (!ok || returned < Marshal.SizeOf<DiskNative.StorageDeviceDescriptor>())
            {
                return (string.Empty, null, null, 0, false);
            }

            var descriptor = Marshal.PtrToStructure<DiskNative.StorageDeviceDescriptor>(buffer);

            string vendor = AnsiAt(buffer, descriptor.VendorIdOffset, bufferSize);
            string product = AnsiAt(buffer, descriptor.ProductIdOffset, bufferSize);

            // Bazı sürücüler markayı ayrı alanda veriyor, bazıları ürün adına gömüyor.
            string model = string.IsNullOrWhiteSpace(vendor) ? product : $"{vendor} {product}".Trim();

            return (
                model,
                Blank(AnsiAt(buffer, descriptor.SerialNumberOffset, bufferSize)),
                Blank(AnsiAt(buffer, descriptor.ProductRevisionOffset, bufferSize)),
                descriptor.BusType,
                descriptor.RemovableMedia);
        }
        finally
        {
            Marshal.FreeHGlobal(query);
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Arama gecikmesi yoksa katı hal disk. Windows'un kendi ölçütü.</summary>
    private static bool ReadIsSolidState(SafeFileHandle handle)
    {
        IntPtr query = Marshal.AllocHGlobal(DiskNative.PropertyQueryHeaderSize + 4);
        IntPtr buffer = Marshal.AllocHGlobal(64);

        try
        {
            Marshal.WriteInt32(query, 0, (int)DiskNative.StorageDeviceSeekPenaltyProperty);
            Marshal.WriteInt32(query, 4, (int)DiskNative.PropertyStandardQuery);
            Marshal.WriteInt32(query, 8, 0);

            bool ok = DiskNative.DeviceIoControl(
                handle,
                DiskNative.IoctlStorageQueryProperty,
                query,
                DiskNative.PropertyQueryHeaderSize + 4,
                buffer,
                64,
                out int returned,
                IntPtr.Zero);

            if (!ok || returned < Marshal.SizeOf<DiskNative.DeviceSeekPenaltyDescriptor>())
            {
                return false;
            }

            return !Marshal.PtrToStructure<DiskNative.DeviceSeekPenaltyDescriptor>(buffer).IncursSeekPenalty;
        }
        finally
        {
            Marshal.FreeHGlobal(query);
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Kapasite geometri sorgusundan okunuyor. IOCTL_DISK_GET_LENGTH_INFO daha
    /// doğrudan görünüyor ama okuma hakkı istiyor; geometri sıfır erişimle çalışıyor,
    /// böylece kapasite yönetici olmadan da görünüyor.
    /// </summary>
    private static long ReadCapacity(SafeFileHandle handle)
    {
        const int bufferSize = 64;

        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            bool ok = DiskNative.DeviceIoControl(
                handle,
                DiskNative.IoctlDiskGetDriveGeometryEx,
                IntPtr.Zero,
                0,
                buffer,
                bufferSize,
                out int returned,
                IntPtr.Zero);

            return ok && returned >= DiskNative.DiskGeometryExSizeOffset + 8
                ? Marshal.ReadInt64(buffer, DiskNative.DiskGeometryExSizeOffset)
                : 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string AnsiAt(IntPtr buffer, uint offset, int bufferSize)
    {
        if (offset == 0 || offset >= bufferSize)
        {
            return string.Empty;
        }

        return Marshal.PtrToStringAnsi(buffer + (int)offset)?.Trim() ?? string.Empty;
    }

    private static string? Blank(string value) => value.Length > 0 ? value : null;

    private static bool IsElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();

            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
